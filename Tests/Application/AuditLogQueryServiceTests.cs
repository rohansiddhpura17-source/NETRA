using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Audit;
using NETRA.Domain.Audit;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="AuditLogQueryService"/>, the read side of the existing
/// append-only trail: newest-first paging, the five filters that map to real columns, the
/// inclusive upper date bound, actor resolution, the filter pick lists and the full detail
/// record. No test here inserts anything other than what <see cref="AuditService"/> would
/// write, and the service itself never mutates the trail.
/// </summary>
/// <remarks>
/// Runs on EF Core's in-memory provider; these are not live SQL Server tests. Timestamps are
/// fixed so the ordering assertions cannot drift with the wall clock.
/// </remarks>
public sealed class AuditLogQueryServiceTests : IDisposable
{
    private static readonly DateTime Base = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly NetraDbContext _context;
    private readonly Guid _firstUserId;
    private readonly Guid _secondUserId;
    private readonly Guid _centralCaseId;
    private readonly Guid _evidenceId;

    public AuditLogQueryServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        _firstUserId = Guid.NewGuid();
        _secondUserId = Guid.NewGuid();
        _centralCaseId = Guid.NewGuid();
        _evidenceId = Guid.NewGuid();

        AddUser(_firstUserId, "reader@netra.local", "Reada", "Well");
        AddUser(_secondUserId, "writer@netra.local", "Wri", "Taylora");
    }

    public void Dispose() => _context.Dispose();

    // ------------------------------------------------------------------------------ fixtures

    private void AddUser(Guid id, string userName, string firstName, string lastName)
    {
        _context.Users.Add(new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = userName,
            NormalizedEmail = userName.ToUpperInvariant(),
            FirstName = firstName,
            LastName = lastName,
            IsActive = true
        });
        _context.SaveChanges();
    }

    private Guid AddEntry(
        string entityType,
        Guid entityId,
        string action,
        Guid userId,
        DateTime timestamp,
        string? propertyName = null,
        string? oldValues = null,
        string? newValues = null,
        bool isSystemAction = false)
    {
        var id = Guid.NewGuid();
        _context.AuditLogs.Add(new AuditLog
        {
            Id = id,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            UserId = userId,
            Timestamp = timestamp,
            PropertyName = propertyName,
            OldValues = oldValues,
            NewValues = newValues,
            IpAddress = "203.0.113.7",
            UserAgent = "NETRA test runner",
            CorrelationId = "corr-0001",
            IsSystemAction = isSystemAction
        });
        _context.SaveChanges();
        return id;
    }

    private AuditLogQueryService CreateService() => new(_context);

    // ------------------------------------------------------------------------ ordering: §14

    [Fact]
    public async Task List_ReturnsTheNewestEntriesFirstAndResolvesTheActor()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base.AddMinutes(-30));
        AddEntry(AuditEntityTypeNames.Evidence, _evidenceId, AuditActionNames.EvidenceUploaded,
            _secondUserId, Base.AddMinutes(-10));
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Edited,
            _firstUserId, Base.AddMinutes(-1));

        var paged = await CreateService().ListAsync(new AuditLogFilterCriteria());

        paged.TotalCount.Should().Be(3);
        paged.Items.Select(item => item.Action).Should().Equal(
            AuditActionNames.Edited, AuditActionNames.EvidenceUploaded, AuditActionNames.Created);
        paged.Items[0].Timestamp.Should().Be(Base.AddMinutes(-1));
        paged.Items[0].ActorName.Should().Be("Reada Well");
        paged.Items[0].ActorUserName.Should().Be("reader@netra.local");
        paged.Items[1].ActorName.Should().Be("Wri Taylora");
        paged.Items[0].EntityId.Should().Be(_centralCaseId);
        paged.Items[0].IsSystemAction.Should().BeFalse();
    }

    // -------------------------------------------------------------------------- paging: §16

    [Fact]
    public async Task List_PagesWithoutRepeatingOrSkippingAnyEntry()
    {
        for (var minute = 0; minute < 7; minute++)
        {
            AddEntry(AuditEntityTypeNames.CrimeCase, Guid.NewGuid(), AuditActionNames.Created,
                _firstUserId, Base.AddMinutes(minute));
        }

        var service = CreateService();
        var first = await service.ListAsync(new AuditLogFilterCriteria { PageSize = 3, Page = 1 });
        var second = await service.ListAsync(new AuditLogFilterCriteria { PageSize = 3, Page = 2 });
        var third = await service.ListAsync(new AuditLogFilterCriteria { PageSize = 3, Page = 3 });

        first.TotalCount.Should().Be(7);
        first.Items.Should().HaveCount(3);
        third.Items.Should().HaveCount(1);

        first.From.Should().Be(1);
        first.To.Should().Be(3);
        third.From.Should().Be(7);
        third.TotalPages.Should().Be(3);

        var seen = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList();
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(7);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-20, 1)]
    [InlineData(250, 100)]
    public async Task List_ClampsWithinTheDefaultAndMaximumPageSizes(int requested, int expected)
    {
        for (var minute = 0; minute < 4; minute++)
        {
            AddEntry(AuditEntityTypeNames.CrimeCase, Guid.NewGuid(), AuditActionNames.Created,
                _firstUserId, Base.AddMinutes(minute));
        }

        var paged = await CreateService().ListAsync(
            new AuditLogFilterCriteria { PageSize = requested });

        paged.PageSize.Should().Be(expected);
        paged.Items.Should().HaveCountLessOrEqualTo(4);
    }

    // ---------------------------------------------------------------------------- filters: §15

    [Fact]
    public async Task List_ActorFilterSelectsOnlyThatAccountsEntries()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base);
        AddEntry(AuditEntityTypeNames.Evidence, _evidenceId, AuditActionNames.EvidenceUploaded,
            _secondUserId, Base.AddMinutes(1));

        var paged = await CreateService().ListAsync(
            new AuditLogFilterCriteria { ActorUserId = _secondUserId });

        paged.TotalCount.Should().Be(1);
        paged.Items.Single().ActorUserId.Should().Be(_secondUserId);
    }

    [Fact]
    public async Task List_EntityTypeAndActionFiltersAreExactMatches()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base);
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Edited,
            _firstUserId, Base.AddMinutes(1));
        AddEntry(AuditEntityTypeNames.Evidence, _evidenceId, AuditActionNames.Created,
            _secondUserId, Base.AddMinutes(2));

        var byType = await CreateService().ListAsync(
            new AuditLogFilterCriteria { EntityType = AuditEntityTypeNames.Evidence });
        var byAction = await CreateService().ListAsync(
            new AuditLogFilterCriteria { Action = AuditActionNames.Edited });
        var byBoth = await CreateService().ListAsync(new AuditLogFilterCriteria
        {
            EntityType = AuditEntityTypeNames.CrimeCase,
            Action = AuditActionNames.Created
        });
        var byUnknown = await CreateService().ListAsync(
            new AuditLogFilterCriteria { Action = "Teleported" });

        byType.Items.Should().ContainSingle();
        byAction.Items.Should().ContainSingle()
            .Which.EntityType.Should().Be(AuditEntityTypeNames.CrimeCase);
        byBoth.TotalCount.Should().Be(1);
        byUnknown.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_DateRangeIncludesTheWholeFinalDayAndBothBoundaryEntries()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, Guid.NewGuid(), AuditActionNames.Created,
            _firstUserId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        AddEntry(AuditEntityTypeNames.CrimeCase, Guid.NewGuid(), AuditActionNames.Edited,
            _firstUserId, new DateTime(2026, 5, 14, 23, 59, 59, DateTimeKind.Utc));
        AddEntry(AuditEntityTypeNames.CrimeCase, Guid.NewGuid(), AuditActionNames.InvestigatorAssigned,
            _firstUserId, new DateTime(2026, 5, 15, 0, 0, 1, DateTimeKind.Utc));
        AddEntry(AuditEntityTypeNames.CrimeCase, Guid.NewGuid(), AuditActionNames.Created,
            _firstUserId, new DateTime(2026, 4, 30, 23, 0, 0, DateTimeKind.Utc));

        var paged = await CreateService().ListAsync(new AuditLogFilterCriteria
        {
            DateFrom = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            DateTo = new DateTime(2026, 5, 14, 0, 0, 0, DateTimeKind.Utc)
        });

        paged.TotalCount.Should().Be(2, "the upper bound is inclusive of the whole day it names");
        paged.Items.Select(item => item.Action).Should().BeEquivalentTo(
            [AuditActionNames.Created, AuditActionNames.Edited]);
    }

    [Fact]
    public async Task List_FiltersCombineIntoOneNarrowedPage()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base);
        AddEntry(AuditEntityTypeNames.Evidence, _evidenceId, AuditActionNames.EvidenceUploaded,
            _secondUserId, Base.AddHours(3));
        AddEntry(AuditEntityTypeNames.Evidence, _evidenceId, AuditActionNames.EvidenceUploaded,
            _firstUserId, Base.AddHours(6));

        var paged = await CreateService().ListAsync(new AuditLogFilterCriteria
        {
            ActorUserId = _firstUserId,
            EntityType = AuditEntityTypeNames.Evidence,
            Action = AuditActionNames.EvidenceUploaded,
            DateFrom = Base.AddHours(1),
            DateTo = Base.AddHours(12)
        });

        paged.TotalCount.Should().Be(1);
        paged.Items.Single().Timestamp.Should().Be(Base.AddHours(6));
    }

    // ---------------------------------------------------------------------- payloads: §17

    [Fact]
    public async Task List_CarriesPropertyNameAndPayloadsForTheSummaryColumn()
    {
        AddEntry(
            AuditEntityTypeNames.Account,
            _firstUserId,
            AuditActionNames.UserEdited,
            _secondUserId,
            Base,
            propertyName: "Roles",
            oldValues: @"First name: ""Ed""",
            newValues: @"First name: ""Edward""");

        var item = (await CreateService().ListAsync(new AuditLogFilterCriteria())).Items.Single();

        item.PropertyName.Should().Be("Roles");
        item.OldValues.Should().Be(@"First name: ""Ed""");
        item.NewValues.Should().Contain("Edward");
    }

    [Fact]
    public async Task List_ReportsSystemActionsSeparatelyFromSignedInActors()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base, isSystemAction: true);

        var item = (await CreateService().ListAsync(new AuditLogFilterCriteria())).Items.Single();

        item.IsSystemAction.Should().BeTrue();
    }

    [Fact]
    public async Task List_WhenTheActorIsNoLongerInTheUserTableLeavesTheNameUnresolved()
    {
        // The trail must still be readable after an account disappears: the query never joins
        // it away, it simply has no name to show.
        var vanished = Guid.NewGuid();
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            vanished, Base);

        var item = (await CreateService().ListAsync(new AuditLogFilterCriteria())).Items.Single();

        item.ActorUserId.Should().Be(vanished);
        item.ActorName.Should().BeNull();
    }

    // ------------------------------------------------------------------------- detail: §17

    [Fact]
    public async Task GetDetail_ReturnsTheCompleteRecordIncludingRequestContext()
    {
        var id = AddEntry(
            AuditEntityTypeNames.ImportJob,
            _centralCaseId,
            AuditActionNames.ImportCompleted,
            _firstUserId,
            Base,
            propertyName: "ImportedRows",
            oldValues: "0",
            newValues: "128");

        var detail = await CreateService().GetDetailAsync(id);

        detail!.Id.Should().Be(id);
        detail.EntityType.Should().Be(AuditEntityTypeNames.ImportJob);
        detail.Action.Should().Be(AuditActionNames.ImportCompleted);
        detail.ActorName.Should().Be("Reada Well");
        detail.Timestamp.Should().Be(Base);
        detail.IpAddress.Should().Be("203.0.113.7");
        detail.UserAgent.Should().Be("NETRA test runner");
        detail.CorrelationId.Should().Be("corr-0001");
        detail.SessionId.Should().BeNull();
        detail.NewValues.Should().Be("128");
    }

    [Fact]
    public async Task GetDetail_UnknownIdentifierReturnsNull()
    {
        (await CreateService().GetDetailAsync(Guid.NewGuid())).Should().BeNull();
    }

    // --------------------------------------------------------------------- filter options

    [Fact]
    public async Task GetFilterOptions_ListsOnlyValuesThatOccurInTheTrail()
    {
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base);
        AddEntry(AuditEntityTypeNames.Evidence, _evidenceId, AuditActionNames.EvidenceUploaded,
            _secondUserId, Base.AddMinutes(1));
        AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base.AddMinutes(2));

        var options = await CreateService().GetFilterOptionsAsync();

        options.EntityTypes.Should().BeEquivalentTo(
            [AuditEntityTypeNames.CrimeCase, AuditEntityTypeNames.Evidence]);
        options.Actions.Should().BeEquivalentTo(
            [AuditActionNames.Created, AuditActionNames.EvidenceUploaded]);
        options.Actors.Select(actor => actor.DisplayName).Should().BeEquivalentTo(
            ["Reada Well", "Wri Taylora"]);
        options.Actors.Should().OnlyContain(actor => actor.UserId != Guid.Empty);
    }

    [Fact]
    public async Task GetFilterOptions_OnAnEmptyTrailOffersNoFilters()
    {
        var options = await CreateService().GetFilterOptionsAsync();

        options.Actors.Should().BeEmpty();
        options.EntityTypes.Should().BeEmpty();
        options.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFilterOptions_IsBoundedSoThePickListStaysUsable()
    {
        for (var i = 0; i < AuditLogLimits.MaxFilterOptions + 40; i++)
        {
            AddEntry($"Type{i:000}", Guid.NewGuid(), $"Action{i:000}", _firstUserId,
                Base.AddMinutes(i));
        }

        var options = await CreateService().GetFilterOptionsAsync();

        options.EntityTypes.Should().HaveCount(AuditLogLimits.MaxFilterOptions);
        options.Actions.Should().HaveCount(AuditLogLimits.MaxFilterOptions);
    }

    // ------------------------------------------------------------------- read-only: §14, §31

    [Fact]
    public async Task TheQueryServiceNeverWritesToTheTrail()
    {
        var id = AddEntry(AuditEntityTypeNames.CrimeCase, _centralCaseId, AuditActionNames.Created,
            _firstUserId, Base);

        var service = CreateService();
        await service.ListAsync(new AuditLogFilterCriteria());
        await service.GetDetailAsync(id);
        await service.GetFilterOptionsAsync();

        (await _context.AuditLogs.CountAsync()).Should().Be(1);

        var untouched = await _context.AuditLogs.AsNoTracking().SingleAsync();
        untouched.Action.Should().Be(AuditActionNames.Created);
        untouched.Timestamp.Should().Be(Base);
        untouched.EntityId.Should().Be(_centralCaseId);
    }
}
