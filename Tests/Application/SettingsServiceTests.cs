using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Application.Alerts;
using NETRA.Application.Settings;
using NETRA.Domain.Audit;
using NETRA.Domain.Enums;
using NETRA.Domain.Security;
using NETRA.Domain.Settings;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Service-level tests for <see cref="SettingsService"/>: the profile fields an officer may
/// change about themselves, the password change path and its Identity rejections, notification
/// and display preferences stored against the existing tables, the read-only administrative
/// listing, and the audit entry each write produces.
/// </summary>
/// <remarks>
/// These run on EF Core's in-memory provider with a real <see cref="UserManager{TUser}"/> and a
/// real <see cref="AuditService"/>, so Identity owns the hashing, the policy checks and the
/// security stamp exactly as it does in the application. They are not live SQL Server tests;
/// the in-memory provider does not enforce the unique indexes, which is why the service
/// pre-checks badge collisions in code.
/// </remarks>
public sealed class SettingsServiceTests : IDisposable
{
    private const string ValidPassword = "ValidPassw0rd!";
    private const string NewValidPassword = "RotationPassw0rd#7";
    private const string Admin = "System Administrator";

    private static readonly DateTimeOffset FixedInstant =
        new(2026, 7, 4, 11, 5, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly NetraDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly Guid _ownerId;

    public SettingsServiceTests()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NetraDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddLogging();
        services.AddIdentityCore<ApplicationUser>(IdentitySettings)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<NetraDbContext>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _context = _scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        _userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var roleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        roleManager.CreateAsync(new IdentityRole<Guid> { Name = Admin }).GetAwaiter().GetResult();

        _ownerId = CreateUserAsync("owning.officer@netra.local", "Anil", "Verma")
            .GetAwaiter().GetResult().Id;
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    // ------------------------------------------------------------------------ test doubles

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedInstant;
    }

    private sealed class StubActorAccessor(Guid id) : IAuditActorAccessor
    {
        public Guid GetUserId() => id;

        public Guid GetUserId(System.Security.Claims.ClaimsPrincipal? principal) => id;
    }

    /// <summary>Mirrors the password policy registered in <c>Program</c>.</summary>
    private static void IdentitySettings(IdentityOptions options)
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.User.RequireUniqueEmail = true;
    }

    /// <summary>
    /// Builds the service as the account it is acting on, matching the controllers, which always
    /// pass the authenticated actor's own id.
    /// </summary>
    private SettingsService CreateService() => CreateServiceFor(_ownerId);

    private SettingsService CreateServiceFor(Guid actorId) => new(
        _context,
        _userManager,
        new AuditService(
            _context,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            new StubActorAccessor(actorId)),
        new FixedTimeProvider());

    // ------------------------------------------------------------------------------ fixtures

    private async Task<ApplicationUser> CreateUserAsync(
        string userName, string firstName, string lastName, string? badge = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = userName,
            FirstName = firstName,
            LastName = lastName,
            BadgeNumber = badge,
            Rank = "Constable",
            EmailConfirmed = true,
            IsActive = true,
            CreatedAt = FixedInstant.UtcDateTime
        };

        (await _userManager.CreateAsync(user, ValidPassword)).Succeeded.Should().BeTrue();
        return user;
    }

    private static UpdateProfileInput Profile(
        string firstName, string lastName, string? badge = null, string? rank = null) => new()
        {
            FirstName = firstName,
            LastName = lastName,
            BadgeNumber = badge,
            Rank = rank
        };

    private static ChangePasswordInput Passwords(string current, string @new, string? confirm = null) => new()
    {
        CurrentPassword = current,
        NewPassword = @new,
        ConfirmPassword = confirm ?? @new
    };

    private static UpdateNotificationPreferencesInput Preferences(
        params (NotificationCategory Category, bool Enabled)[] choices) => new()
        {
            EnabledByCategory = choices.ToDictionary(choice => choice.Category, choice => choice.Enabled)
        };

    private async Task<List<AuditLog>> TrailAsync(Guid entityId) =>
        await _context.AuditLogs.AsNoTracking()
            .Where(entry => entry.EntityId == entityId)
            .OrderBy(entry => entry.Timestamp)
            .ToListAsync();

    private Task<List<NotificationPreference>> RowsForAsync(Guid userId) =>
        _context.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();

    // -------------------------------------------------------------------- profile: §3, §12

    [Fact]
    public async Task GetProfile_ReturnsTheOfficersOwnDetails()
    {
        var profile = await CreateService().GetProfileAsync(_ownerId);

        profile!.UserName.Should().Be("owning.officer@netra.local");
        profile.FirstName.Should().Be("Anil");
        profile.LastName.Should().Be("Verma");
        profile.Rank.Should().Be("Constable");
        profile.BadgeNumber.Should().BeNull();
        profile.MemberSince.Should().Be(FixedInstant.UtcDateTime);
        profile.IsActive.Should().BeTrue();
        profile.FullName.Should().Be("Anil Verma");
    }

    [Fact]
    public async Task GetProfile_WithoutAnAccountIsHonestAboutIt()
    {
        (await CreateService().GetProfileAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public void ProfileDto_CarriesNoCredentialFieldsForRazorToLeak() =>
        ForbiddenNames.In(typeof(ProfileDto)).Should().BeEmpty();

    [Fact]
    public async Task UpdateProfile_WritesTheEditedFieldsAndRecordsEachPropertyChange()
    {
        var result = await CreateService().UpdateProfileAsync(
            _ownerId, Profile("Anil", "Verma", badge: "MH-4471", rank: "Senior PSIC"));

        result.Succeeded.Should().BeTrue();

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        user!.BadgeNumber.Should().Be("MH-4471");
        user.Rank.Should().Be("Senior PSIC");
        user.FirstName.Should().Be("Anil");

        var trail = await TrailAsync(_ownerId);
        trail.Should().HaveCount(2);
        trail.Should().AllSatisfy(entry =>
        {
            entry.EntityType.Should().Be(AuditEntityTypeNames.Account);
            entry.Action.Should().Be(AuditActionNames.UserEdited);
            entry.UserId.Should().Be(_ownerId);
        });

        var badge = trail.Single(entry => entry.PropertyName == "BadgeNumber");
        badge.OldValues.Should().BeNull();
        badge.NewValues.Should().Be("MH-4471");
        trail.Single(entry => entry.PropertyName == "Rank")
            .OldValues.Should().Be("Constable");
    }

    [Fact]
    public async Task UpdateProfile_WithNothingChangedWritesNoAuditNoise()
    {
        var service = CreateService();
        await service.UpdateProfileAsync(_ownerId, Profile("Anil", "Verma", rank: "Constable"));

        await service.UpdateProfileAsync(_ownerId, Profile("Anil", "Verma", rank: "Constable"));

        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateProfile_TrimsInputAndStoresBlankOptionalFieldsAsNothing()
    {
        var first = CreateService();
        await first.UpdateProfileAsync(_ownerId, Profile("  Anil  ", "  Verma  ", "  ", "  "));

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        user!.FirstName.Should().Be("Anil");
        user.BadgeNumber.Should().BeNull();
        user.Rank.Should().BeNull();
    }

    [Theory]
    [InlineData("", "Verma", "FirstName")]
    [InlineData("Anil", "   ", "LastName")]
    public async Task UpdateProfile_RequiresBothNamesAgainstTheFieldThatIsMissing(
        string firstName, string lastName, string expectedField)
    {
        var result = await CreateService().UpdateProfileAsync(
            _ownerId, Profile(firstName, lastName));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain(expectedField);

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        user!.FirstName.Should().Be("Anil");
        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateProfile_RejectsAnOverlongBadgeAndRank()
    {
        var result = await CreateService().UpdateProfileAsync(_ownerId,
            Profile("Anil", "Verma", new string('B', 51), new string('R', 101)));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().BeEquivalentTo(["BadgeNumber", "Rank"]);
    }

    [Fact]
    public async Task UpdateProfile_RefusesABadgeAnotherAccountAlreadyUses()
    {
        await CreateUserAsync("other.officer@netra.local", "Kavita", "Rao", badge: "MH-9900");

        var result = await CreateService().UpdateProfileAsync(
            _ownerId, Profile("Anil", "Verma", badge: "MH-9900"));

        result.Status.Should().Be(OperationStatus.Conflict);
        result.Errors["BadgeNumber"].Should().ContainSingle()
            .Which.Should().Contain("already uses");

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        user!.BadgeNumber.Should().BeNull();
        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateProfile_KeepingYourOwnBadgeIsNotAClashWithYourself()
    {
        await CreateService().UpdateProfileAsync(_ownerId, Profile("Anil", "Verma", "MH-1234"));

        var again = await CreateService().UpdateProfileAsync(
            _ownerId, Profile("Anil", "Verma", "MH-1234", rank: "PSI"));

        again.Succeeded.Should().BeTrue();
        (await _userManager.FindByIdAsync(_ownerId.ToString()))!
            .BadgeNumber.Should().Be("MH-1234");
    }

    [Fact]
    public async Task UpdateProfile_UnknownAccountIsReportedAsMissingNotFailedValidation()
    {
        var result = await CreateService().UpdateProfileAsync(
            Guid.NewGuid(), Profile("Someone", "Else"));

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task UpdateProfile_ReachesOnlyTheAccountTheCallerNamed()
    {
        var other = await CreateUserAsync("second.officer@netra.local", "Sunita", "Pillai");

        await CreateService().UpdateProfileAsync(
            _ownerId, Profile("Anil", "Verma", badge: "MH-777", rank: "Inspector"));

        var untouched = await _userManager.FindByIdAsync(other.Id.ToString());
        untouched!.BadgeNumber.Should().BeNull();
        untouched.Rank.Should().Be("Constable");

        (await TrailAsync(other.Id)).Should().BeEmpty();
    }

    // -------------------------------------------------------------------- security: §4

    [Fact]
    public async Task GetSecurityStatus_ReflectsThePolicyIdentityActuallyEnforces()
    {
        var status = await CreateService().GetSecurityStatusAsync(_ownerId);

        status!.HasPassword.Should().BeTrue();
        status.EmailConfirmed.Should().BeTrue();
        status.TwoFactorEnabled.Should().BeFalse();
        status.IsLockedOut.Should().BeFalse();
        status.LockoutExpiresAt.Should().BeNull();
        status.PasswordPolicy.RequiredLength.Should().Be(8);
        status.PasswordPolicy.RequireNonAlphanumeric.Should().BeTrue();
    }

    [Fact]
    public async Task GetSecurityStatus_WithoutAnAccountIsHonestAboutIt()
    {
        (await CreateService().GetSecurityStatusAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public void SecurityStatusDto_CarriesNoCredentialFieldsForRazorToLeak() =>
        ForbiddenNames.In(typeof(SecurityStatusDto)).Should().BeEmpty();

    [Fact]
    public async Task ChangePassword_SetsTheNewCredentialAndRecordsOnlyThatItHappened()
    {
        var result = await CreateService().ChangePasswordAsync(
            _ownerId, Passwords(ValidPassword, NewValidPassword));

        result.Succeeded.Should().BeTrue();

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        (await _userManager.CheckPasswordAsync(user!, NewValidPassword)).Should().BeTrue();
        (await _userManager.CheckPasswordAsync(user!, ValidPassword)).Should().BeFalse();

        var trail = await TrailAsync(_ownerId);
        var entry = trail.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditActionNames.PasswordChanged);
        entry.EntityType.Should().Be(AuditEntityTypeNames.Account);
        entry.UserId.Should().Be(_ownerId);
        entry.NewValues.Should().Be("Password changed by the account owner.");

        // Neither the old nor the new credential appears anywhere in the stored trail.
        trail.NotContainPasswords(ValidPassword, NewValidPassword);
    }

    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPasswordLandsOnThatField()
    {
        var result = await CreateService().ChangePasswordAsync(
            _ownerId, Passwords("WrongGuess1!", NewValidPassword));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain("CurrentPassword");

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        (await _userManager.CheckPasswordAsync(user!, ValidPassword)).Should().BeTrue();
        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_WithMismatchedConfirmationLandsOnTheConfirmationField()
    {
        var result = await CreateService().ChangePasswordAsync(
            _ownerId, Passwords(ValidPassword, NewValidPassword, confirm: "RotationPassw0rd#8"));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["ConfirmPassword"].Should().ContainSingle()
            .Which.Should().Be("The two passwords do not match.");

        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_RejectedByThePolicyLandsOnTheNewPasswordField()
    {
        var result = await CreateService().ChangePasswordAsync(
            _ownerId, Passwords(ValidPassword, "weak"));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().Contain("NewPassword");

        var user = await _userManager.FindByIdAsync(_ownerId.ToString());
        (await _userManager.CheckPasswordAsync(user!, ValidPassword)).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_BlankFieldsAreRefusedBeforeIdentityIsConsulted()
    {
        var result = await CreateService().ChangePasswordAsync(
            _ownerId, Passwords("", "", confirm: ""));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors.Keys.Should().BeEquivalentTo(["CurrentPassword", "NewPassword"]);
        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangePassword_UnknownAccountIsReportedAsMissing()
    {
        var result = await CreateServiceFor(Guid.NewGuid()).ChangePasswordAsync(
            Guid.NewGuid(), Passwords(ValidPassword, NewValidPassword));

        result.Status.Should().Be(OperationStatus.NotFound);
    }

    // ---------------------------------------------------------------- notifications: §5

    [Fact]
    public async Task NotificationPreferences_DefaultToEveryRealCategoryEnabled()
    {
        var rows = await CreateService().GetNotificationPreferencesAsync(_ownerId);

        rows.Select(row => row.Category).Should().BeEquivalentTo(Enum.GetValues<NotificationCategory>());
        rows.Should().AllSatisfy(row =>
        {
            row.IsEnabled.Should().BeTrue();
            row.HasStoredRow.Should().BeFalse();
            row.Label.Should().Be(NotificationCategories.Label(row.Category));
        });
    }

    [Fact]
    public async Task DisablingACategoryStoresOneInApplicationRowAndRecordsTheChoice()
    {
        var result = await CreateService().UpdateNotificationPreferencesAsync(
            _ownerId, Preferences((NotificationCategory.System, false)));

        result.Succeeded.Should().BeTrue();

        var row = (await RowsForAsync(_ownerId)).Should().ContainSingle().Subject;
        row.NotificationType.Should().Be((int)NotificationCategory.System);
        row.DeliveryChannel.Should().Be(SettingsService.InApplicationDeliveryChannel);
        row.IsEnabled.Should().BeFalse();
        row.UpdatedAt.Should().Be(FixedInstant.UtcDateTime);

        var entry = (await TrailAsync(_ownerId)).Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditActionNames.NotificationPreferencesUpdated);
        entry.PropertyName.Should().Be("Notifications.System");
        entry.OldValues.Should().Be("Enabled");
        entry.NewValues.Should().Be("Disabled");
    }

    [Fact]
    public async Task ReEnablingACategoryUpdatesTheRowInsteadOfStackingAnother()
    {
        _context.NotificationPreferences.Add(new NotificationPreference
        {
            UserId = _ownerId,
            NotificationType = (int)NotificationCategory.Investigation,
            DeliveryChannel = SettingsService.InApplicationDeliveryChannel,
            IsEnabled = false,
            CreatedAt = FixedInstant.UtcDateTime,
            UpdatedAt = FixedInstant.UtcDateTime
        });
        await _context.SaveChangesAsync();

        var result = await CreateService().UpdateNotificationPreferencesAsync(
            _ownerId, Preferences((NotificationCategory.Investigation, true)));

        result.Succeeded.Should().BeTrue();
        var row = (await RowsForAsync(_ownerId)).Should().ContainSingle().Subject;
        row.IsEnabled.Should().BeTrue();

        var entries = await TrailAsync(_ownerId);
        entries.Should().ContainSingle()
            .Which.PropertyName.Should().Be("Notifications.Investigation");

        var effective = await CreateService().GetNotificationPreferencesAsync(_ownerId);
        effective.Single(current => current.Category == NotificationCategory.Investigation)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task LeavingTheDefaultStateUnchangedWritesNothing()
    {
        var result = await CreateService().UpdateNotificationPreferencesAsync(
            _ownerId, Preferences(
                (NotificationCategory.CrimeIntelligence, true),
                (NotificationCategory.Investigation, true),
                (NotificationCategory.System, true)));

        result.Succeeded.Should().BeTrue();
        (await RowsForAsync(_ownerId)).Should().BeEmpty();
        (await TrailAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task OneSubmissionCanSplitCategoriesAcrossEnabledAndDisabled()
    {
        var result = await CreateService().UpdateNotificationPreferencesAsync(
            _ownerId, Preferences(
                (NotificationCategory.CrimeIntelligence, false),
                (NotificationCategory.Investigation, true),
                (NotificationCategory.System, false)));

        result.Succeeded.Should().BeTrue();
        (await RowsForAsync(_ownerId)).Should().HaveCount(2)
            .And.NotContain(row => row.NotificationType == (int)NotificationCategory.Investigation);
        (await TrailAsync(_ownerId)).Should().HaveCount(2);
    }

    [Fact]
    public async Task AnUnknownCategoryIsRefusedAndWritesNothing()
    {
        var result = await CreateService().UpdateNotificationPreferencesAsync(
            _ownerId, Preferences(((NotificationCategory)99, false)));

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["EnabledCategories"].Should().ContainSingle().Which.Should().Contain("Unknown");
        (await RowsForAsync(_ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task NotificationPreferences_WithoutAnActorAreRefused()
    {
        var result = await CreateServiceFor(Guid.Empty).UpdateNotificationPreferencesAsync(
            Guid.Empty, Preferences((NotificationCategory.System, false)));

        result.Status.Should().Be(OperationStatus.Forbidden);
    }

    // -------------------------------------------------------------------- appearance: §6

    [Fact]
    public async Task Appearance_DefaultsToTheThemeWhenNothingIsStored()
    {
        var appearance = await CreateService().GetAppearanceAsync(_ownerId);

        appearance.Density.Should().Be(UiDensity.Comfortable);
        appearance.ReducedMotion.Should().BeFalse();
    }

    [Fact]
    public async Task Appearance_WithoutAnActorIsTheDefaultNotADatabaseRead()
    {
        var appearance = await CreateServiceFor(Guid.Empty).GetAppearanceAsync(Guid.Empty);

        appearance.Density.Should().Be(UiDensity.Comfortable);
        appearance.ReducedMotion.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAppearance_StoresTwoRowsInUserSettingsAndRecordsEachChoice()
    {
        var result = await CreateService().UpdateAppearanceAsync(
            _ownerId, new UpdateAppearanceInput
            {
                Density = UiDensity.Compact,
                ReducedMotion = true
            });

        result.Succeeded.Should().BeTrue();

        var rows = await _context.UserSettings.AsNoTracking()
            .Where(s => s.UserId == _ownerId)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().AllSatisfy(row =>
        {
            row.Category.Should().Be(SettingsService.AppearanceSettingCategory);
            row.UpdatedAt.Should().Be(FixedInstant.UtcDateTime);
        });
        rows.Single(r => r.Key == SettingsService.DensitySettingKey).Value.Should().Be("Compact");
        rows.Single(r => r.Key == SettingsService.ReducedMotionSettingKey)
            .Value.Should().Be(bool.TrueString);

        var trail = await TrailAsync(_ownerId);
        trail.Should().HaveCount(2);
        trail.Should().OnlyContain(entry =>
            entry.Action == AuditActionNames.AppearancePreferencesUpdated);
        trail.Single(e => e.PropertyName == "Density").NewValues.Should().Be("Compact");
    }

    [Fact]
    public async Task UpdateAppearance_ASecondIdenticalSubmissionChangesNoRowAndAddsNoTrail()
    {
        var service = CreateService();
        var input = new UpdateAppearanceInput { Density = UiDensity.Compact, ReducedMotion = false };

        await service.UpdateAppearanceAsync(_ownerId, input);
        var firstTrailCount = (await TrailAsync(_ownerId)).Count;

        await service.UpdateAppearanceAsync(_ownerId, input);

        firstTrailCount.Should().Be(1);
        (await TrailAsync(_ownerId)).Should().HaveCount(1);
        (await _context.UserSettings.AsNoTracking().CountAsync(
            s => s.UserId == _ownerId)).Should().Be(1);
    }

    [Fact]
    public async Task UpdateAppearance_RejectsAnUndefinedDensityWithoutTouchingStorage()
    {
        var result = await CreateService().UpdateAppearanceAsync(
            _ownerId, new UpdateAppearanceInput { Density = (UiDensity)7, ReducedMotion = false });

        result.Status.Should().Be(OperationStatus.ValidationFailed);
        result.Errors["Density"].Should().ContainSingle().Which.Should().Contain("Comfortable");
        (await _context.UserSettings.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Appearance_UnparseableStoredValuesFallBackToTheDefaults()
    {
        _context.UserSettings.AddRange(
            new UserSetting
            {
                UserId = _ownerId,
                Key = SettingsService.DensitySettingKey,
                Value = "sideways",
                Category = SettingsService.AppearanceSettingCategory
            },
            new UserSetting
            {
                UserId = _ownerId,
                Key = SettingsService.ReducedMotionSettingKey,
                Value = "maybe",
                Category = SettingsService.AppearanceSettingCategory
            });
        await _context.SaveChangesAsync();

        var appearance = await CreateService().GetAppearanceAsync(_ownerId);

        appearance.Density.Should().Be(UiDensity.Comfortable);
        appearance.ReducedMotion.Should().BeFalse();
    }

    [Fact]
    public async Task Appearance_WithoutAnActorIsRefusedOnWrite()
    {
        var result = await CreateServiceFor(Guid.Empty).UpdateAppearanceAsync(
            Guid.Empty,
            new UpdateAppearanceInput { Density = UiDensity.Compact, ReducedMotion = false });

        result.Status.Should().Be(OperationStatus.Forbidden);
    }

    [Fact]
    public async Task Appearance_IsPerAccountNotSharedBetweenOfficers()
    {
        var other = await CreateUserAsync("third.officer@netra.local", "Dev", "Menon");

        await CreateService().UpdateAppearanceAsync(
            _ownerId, new UpdateAppearanceInput { Density = UiDensity.Compact, ReducedMotion = true });

        var theirs = await CreateService().GetAppearanceAsync(other.Id);
        theirs.Density.Should().Be(UiDensity.Comfortable);
        theirs.ReducedMotion.Should().BeFalse();
    }

    // ------------------------------------------------------------------ system settings: §7

    [Fact]
    public async Task SystemSettings_ListsBothTablesOrderedAndCreditsTheUpdater()
    {
        _context.SystemPreferences.Add(new SystemPreference
        {
            Key = "import.batch-size",
            Value = "500",
            Category = "Import",
            Description = "Rows processed per import batch.",
            IsReadOnly = true,
            UpdatedAt = FixedInstant.UtcDateTime,
            UpdatedById = _ownerId
        });
        _context.SecuritySettings.Add(new SecuritySetting
        {
            Key = "session.idle-minutes",
            Value = "30",
            Category = "Session",
            Description = "Idle sign-in lifetime.",
            IsSystemSetting = true,
            UpdatedAt = FixedInstant.UtcDateTime,
            UpdatedById = _ownerId
        });
        await _context.SaveChangesAsync();

        var settings = await CreateService().GetSystemSettingsAsync();

        var preference = settings.Preferences.Should().ContainSingle().Subject;
        preference.Key.Should().Be("import.batch-size");
        preference.Value.Should().Be("500");
        preference.UpdatedBy.Should().Be("owning.officer@netra.local");
        preference.IsSystemOwned.Should().BeTrue();

        settings.SecuritySettings.Should().ContainSingle()
            .Which.Key.Should().Be("session.idle-minutes");
    }

    [Fact]
    public async Task SystemSettings_WithNothingStoredIsTwoEmptyListsNotAnError()
    {
        var settings = await CreateService().GetSystemSettingsAsync();

        settings.Preferences.Should().BeEmpty();
        settings.SecuritySettings.Should().BeEmpty();
    }

    [Fact]
    public async Task SystemSettings_ListingIsCappedSoNoRequestCanReadAnUnboundedTable()
    {
        foreach (var index in Enumerable.Range(0, SettingsService.MaxAdminSettingRows + 5))
        {
            _context.SystemPreferences.Add(new SystemPreference
            {
                Key = $"generated.{index:D4}",
                Value = index.ToString()
            });
        }
        await _context.SaveChangesAsync();

        var settings = await CreateService().GetSystemSettingsAsync();

        settings.Preferences.Should().HaveCount(SettingsService.MaxAdminSettingRows);
        settings.Preferences.Select(p => p.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public void AdminSettingDto_IsOnlyWhatThePageNeedsAndNothingElse()
    {
        typeof(AdminSettingDto).GetProperties()
            .Select(p => p.Name)
            .Should().BeEquivalentTo(
                nameof(AdminSettingDto.Key),
                nameof(AdminSettingDto.Value),
                nameof(AdminSettingDto.Category),
                nameof(AdminSettingDto.Description),
                nameof(AdminSettingDto.UpdatedAt),
                nameof(AdminSettingDto.UpdatedBy),
                nameof(AdminSettingDto.IsSystemOwned));
    }
}

/// <summary>
/// Data-minimization checks: the settings DTOs must not carry the Identity columns that would
/// be dangerous in a rendered page, an audit payload or a log line.
/// </summary>
internal static class ForbiddenNames
{
    private static readonly string[] Forbidden =
    [
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
        "Password", "CurrentPassword", "NewPassword", "ConfirmPassword",
        "ConnectionString", "ApiKey", "Secret"
    ];

    /// <summary>The secret-looking property names present on <paramref name="type"/>.</summary>
    public static IReadOnlyList<string> In(Type type) =>
        [.. type.GetProperties().Select(p => p.Name).Where(Forbidden.Contains)];
}

internal static class AuditTrailAssertions
{
    /// <summary>Asserts no stored audit payload contains any of the given credential values.</summary>
    public static void NotContainPasswords(
        this IEnumerable<AuditLog> entries, params string[] passwords)
    {
        var payloads = entries
            .SelectMany(entry => new[] { entry.OldValues, entry.NewValues })
            .OfType<string>()
            .ToList();

        foreach (var password in passwords)
        {
            payloads.Should().NotContain(value => value.Contains(password, StringComparison.Ordinal));
        }
    }
}
