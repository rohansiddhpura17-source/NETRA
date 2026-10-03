using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="NotificationService"/>. Ownership is the
/// central rule: every query and mutation is scoped to the signed-in recipient
/// inside the database query, foreign ids answer "not found" without leaking
/// existence, and the attempt is audited as a security event.
/// </summary>
public sealed class NotificationServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedInstant = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    private static readonly DateTime FixedNow = FixedInstant.UtcDateTime;

    private readonly NetraDbContext _context;
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _intruderId = Guid.NewGuid();
    private readonly NotificationService _service;

    public NotificationServiceTests()
    {
        _context = new NetraDbContext(
            new DbContextOptionsBuilder<NetraDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        _context.Users.AddRange(
            User(_ownerId, "notify.owner@netra.local"),
            User(_intruderId, "notify.intruder@netra.local"));

        var audit = new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(_ownerId));
        _service = new NotificationService(
            _context, audit, new FixedTimeProvider(FixedInstant));
    }

    public void Dispose() => _context.Dispose();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubActorAccessor : IAuditActorAccessor
    {
        private readonly Guid _id;
        public StubActorAccessor(Guid id) => _id = id;
        public Guid GetUserId() => _id;
        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => _id;
    }

    private ApplicationUser User(Guid id, string email) => new()
    {
        Id = id,
        UserName = email,
        Email = email,
        FirstName = "Notify",
        LastName = id == _ownerId ? "Owner" : "Intruder",
        EmailConfirmed = true,
        IsActive = true
    };

    private Notification Seed(
        Guid recipientId,
        bool isRead = false,
        int hoursAgo = 0,
        AlertSeverity severity = AlertSeverity.High)
    {
        var alert = new Alert
        {
            AlertType = (int)AlertCategory.CrimeSpike,
            Severity = severity,
            Status = AlertStatus.New,
            Title = $"Linked alert {hoursAgo}",
            CreatedAt = FixedNow.AddHours(-hoursAgo)
        };
        var notification = new Notification
        {
            AlertId = alert.Id,
            Alert = alert,
            RecipientId = recipientId,
            Title = alert.Title,
            Message = "Notification body for coverage.",
            NotificationType = (int)NotificationCategory.CrimeIntelligence,
            IsRead = isRead,
            CreatedAt = FixedNow.AddHours(-hoursAgo)
        };
        _context.Alerts.Add(alert);
        _context.Notifications.Add(notification);
        return notification;
    }

    [Fact]
    public async Task GetForUserAsync_ReturnsOnlyOwnedNotifications()
    {
        Seed(_ownerId);
        Seed(_ownerId, isRead: true, hoursAgo: 2);
        var foreign = Seed(_intruderId, hoursAgo: 1);
        await _context.SaveChangesAsync();

        var result = await _service.GetForUserAsync(_ownerId);

        result.Items.Should().HaveCount(2);
        result.Items.Should().NotContain(n => n.Id == foreign.Id);
        result.TotalCount.Should().Be(2);
        result.UnreadCount.Should().Be(1);
        result.Items.First().AlertSeverity.Should().Be(AlertSeverity.High);
        result.Items.First().Category.Should().Be(NotificationCategory.CrimeIntelligence);
    }

    [Fact]
    public async Task GetForUserAsync_UnreadOnlyFilterAndOrdering()
    {
        var older = Seed(_ownerId, hoursAgo: 5);
        var newest = Seed(_ownerId, hoursAgo: 1);
        Seed(_ownerId, isRead: true, hoursAgo: 2);
        await _context.SaveChangesAsync();

        var unread = await _service.GetForUserAsync(_ownerId, unreadOnly: true);

        unread.Items.Should().HaveCount(2);
        unread.Items.Select(i => i.Id).Should().ContainInOrder(newest.Id, older.Id);
        unread.Items.Should().OnlyContain(i => i.IsRead == false);
    }

    [Fact]
    public async Task MarkReadAsync_OwnerUpdatesOnce_AndIsIdempotent()
    {
        var notification = Seed(_ownerId);
        await _context.SaveChangesAsync();

        (await _service.MarkReadAsync(notification.Id, _ownerId)).Succeeded.Should().BeTrue();
        (await _service.MarkReadAsync(notification.Id, _ownerId)).Succeeded.Should().BeTrue();

        var stored = await _context.Notifications.AsNoTracking().FirstAsync(n => n.Id == notification.Id);
        stored.IsRead.Should().BeTrue();
        stored.ReadAt.Should().Be(FixedNow);

        // The second call must not write a second audit row.
        (await _context.AuditLogs.CountAsync(l =>
            l.Action == AuditActionNames.NotificationMarkedRead)).Should().Be(1);
    }

    [Fact]
    public async Task MarkReadAsync_ForeignId_GivesNotFoundAndAuditsSecurityEvent()
    {
        var notification = Seed(_ownerId);
        await _context.SaveChangesAsync();

        var result = await _service.MarkReadAsync(notification.Id, _intruderId);

        result.Succeeded.Should().BeFalse();
        result.Status.Should().Be(OperationStatus.NotFound);
        result.Errors.Values.SelectMany(e => e).Should().ContainSingle()
            .Which.Should().Be("The notification was not found.");

        var stored = await _context.Notifications.AsNoTracking().FirstAsync(n => n.Id == notification.Id);
        stored.IsRead.Should().BeFalse();

        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.NotificationAccessDenied &&
            l.UserId == _intruderId)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkReadAsync_UnknownId_GivesNotFoundWithoutSecurityEvent()
    {
        var result = await _service.MarkReadAsync(Guid.NewGuid(), _ownerId);

        result.Status.Should().Be(OperationStatus.NotFound);
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.NotificationAccessDenied)).Should().BeFalse();
    }

    [Fact]
    public async Task MarkAllReadAsync_OnlyAffectsTheActor()
    {
        Seed(_ownerId);
        Seed(_ownerId, hoursAgo: 1);
        var foreign = Seed(_intruderId, hoursAgo: 2);
        await _context.SaveChangesAsync();

        (await _service.MarkAllReadAsync(_ownerId)).Succeeded.Should().BeTrue();

        (await _context.Notifications.CountAsync(n => n.RecipientId == _ownerId && !n.IsRead))
            .Should().Be(0);
        (await _context.Notifications.FirstAsync(n => n.Id == foreign.Id)).IsRead.Should().BeFalse();

        var inbox = await _service.GetForUserAsync(_ownerId);
        inbox.UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task MarkAllReadAsync_WithNothingUnread_IsANoOp()
    {
        var result = await _service.MarkAllReadAsync(_ownerId);

        result.Succeeded.Should().BeTrue();
        (await _context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.NotificationMarkedRead)).Should().BeFalse();
    }

    [Fact]
    public async Task GetForUserAsync_PagesByFifty_AndReportsFullUnreadCount()
    {
        for (var i = 0; i < 55; i++)
        {
            Seed(_ownerId, hoursAgo: i);
        }

        await _context.SaveChangesAsync();

        var first = await _service.GetForUserAsync(_ownerId);
        var second = await _service.GetForUserAsync(_ownerId, page: 2);

        first.Items.Should().HaveCount(AlertLimits.MaxNotificationPageSize);
        first.PageSize.Should().Be(AlertLimits.MaxNotificationPageSize);
        first.TotalPages.Should().Be(2);
        first.UnreadCount.Should().Be(55);
        second.Items.Should().HaveCount(5);
        second.TotalCount.Should().Be(55);
    }
}
