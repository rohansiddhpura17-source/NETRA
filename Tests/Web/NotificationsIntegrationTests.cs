using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NETRA.Application.Abstractions;
using NETRA.Domain.Alerts;
using NETRA.Domain.Enums;
using NETRA.Infrastructure.Data;
using Xunit;

namespace NETRA.Tests.Web;

/// <summary>
/// Real MVC integration coverage for the /notifications centre: authentication,
/// strict recipient ownership (no userId parameter exists anywhere in the route
/// surface), mark-read and mark-all-read flows, unread counters, honest empty
/// states and the no-JavaScript guarantee. Hosted with EF Core's in-memory
/// provider by <see cref="NetraWebFactory"/>; not live SQL Server tests.
/// </summary>
public sealed class NotificationsIntegrationTests : IClassFixture<NetraWebFactory>
{
    private const string Password = "ValidPassw0rd!";
    private readonly NetraWebFactory _factory;

    public NotificationsIntegrationTests(NetraWebFactory factory)
    {
        _factory = factory;
        _factory.SeedReferenceDataAsync().GetAwaiter().GetResult();
    }

    private IServiceScope NewScope() => _factory.Services.CreateScope();

    private async Task<HttpClient> SignInAsync(string email, params string[] roles)
    {
        await _factory.SeedUserAsync(email, Password, roles);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/Account/Login"));
        var response = await client.PostAsync("/Account/Login", AuthenticationScreenTests.Form(
            ("UserName", email),
            ("Password", Password),
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private HttpClient AnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Seeds an alert plus one addressed notification; returns both ids.</summary>
    private async Task<(Guid NotificationId, Guid AlertId)> SeedInboxAsync(
        string email, string title, bool isRead = false, bool withAlert = true)
    {
        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var user = await context.Users.FirstAsync(u => u.Email == email);

        Alert? alert = null;
        if (withAlert)
        {
            alert = new Alert
            {
                AlertType = (int)AlertCategory.UnusualPattern,
                Severity = AlertSeverity.Critical,
                Status = AlertStatus.New,
                Title = $"{title} (linked alert)",
                CreatedAt = DateTime.UtcNow
            };
            context.Alerts.Add(alert);
        }

        var notification = new Notification
        {
            AlertId = alert?.Id,
            RecipientId = user.Id,
            Title = title,
            Message = "Server-rendered notification body for integration coverage.",
            NotificationType = (int)NotificationCategory.CrimeIntelligence,
            IsRead = isRead,
            CreatedAt = DateTime.UtcNow
        };
        context.Notifications.Add(notification);
        await context.SaveChangesAsync();
        return (notification.Id, alert?.Id ?? Guid.Empty);
    }

    [Fact]
    public async Task Notifications_AnonymousRequests_RedirectToLogin()
    {
        var client = AnonymousClient();

        var index = await client.GetAsync("/notifications");
        var markAll = await client.PostAsync("/notifications/read-all",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        foreach (var response in new[] { index, markAll })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Redirect);
            response.Headers.Location!.ToString().Should().Contain("/Account/Login");
        }
    }

    [Fact]
    public async Task Notifications_AnyAuthenticatedRoleMayOpenTheirOwnInbox()
    {
        foreach (var (email, role) in new[]
                 {
                     ("notif.it.admin@netra.local", "System Administrator"),
                     ("notif.it.analyst@netra.local", "Police / Intelligence Analyst"),
                     ("notif.it.investigator@netra.local", "Investigator")
                 })
        {
            var client = await SignInAsync(email, role);
            await SeedInboxAsync(email, $"NOTIF-IT item for {role}");

            var response = await client.GetAsync("/notifications");
            var html = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            html.Should().Contain($"NOTIF-IT item for {role}");
        }
    }

    [Fact]
    public async Task Notifications_OwnershipIsEnforcedInQueries_OtherUsersAreInvisible()
    {
        var owner = await SignInAsync("notif.it.owner@netra.local", "Police / Intelligence Analyst");
        var intruder = await SignInAsync("notif.it.intruder@netra.local", "System Administrator");

        var (notificationId, _) = await SeedInboxAsync("notif.it.owner@netra.local", "NOTIF-IT private item");

        var intruderHtml = await intruder.GetStringAsync("/notifications");
        intruderHtml.Should().NotContain("NOTIF-IT private item");
        intruderHtml.Should().Contain("No notifications yet");

        var ownerHtml = await owner.GetStringAsync("/notifications");
        ownerHtml.Should().Contain("NOTIF-IT private item");

        // The route surface has no userId parameter at all.
        ownerHtml.Should().NotContain("name=\"userId\"");
        intruderHtml.Should().NotContain("name=\"userId\"");
    }

    [Fact]
    public async Task MarkRead_ForeignId_DoesNotMutateOrExposeTheOwner()
    {
        var intruder = await SignInAsync("notif.it.thief@netra.local", "Senior Officer");
        await SignInAsync("notif.it.victim@netra.local", "Police / Intelligence Analyst");
        var (notificationId, _) = await SeedInboxAsync("notif.it.victim@netra.local", "NOTIF-IT victim item");
        await intruder.GetStringAsync("/notifications");

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await intruder.GetStringAsync("/notifications"));
        var response = await intruder.PostAsync(
            $"/notifications/read/{notificationId}", AuthenticationScreenTests.Form(
                ("page", "1"), ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var followed = await intruder.GetStringAsync(response.Headers.Location!.ToString());
        followed.Should().Contain("The notification was not found.");
        followed.Should().NotContain("NOTIF-IT victim item");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var stored = await context.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);
        stored.IsRead.Should().BeFalse();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.NotificationAccessDenied)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkRead_OwnNotification_IsPersistedAndAudited()
    {
        var client = await SignInAsync("notif.it.reader@netra.local", "Investigator");
        var (notificationId, _) = await SeedInboxAsync("notif.it.reader@netra.local", "NOTIF-IT markable item");

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(
            await client.GetStringAsync("/notifications"));
        var response = await client.PostAsync(
            $"/notifications/read/{notificationId}", AuthenticationScreenTests.Form(
                ("page", "1"), ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var html = await client.GetStringAsync(response.Headers.Location!.ToString());
        html.Should().Contain("Notification marked as read.");
        html.Should().Contain("Read ");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        var stored = await context.Notifications.AsNoTracking().FirstAsync(n => n.Id == notificationId);
        stored.IsRead.Should().BeTrue();
        stored.ReadAt.Should().NotBeNull();
        (await context.AuditLogs.AnyAsync(l =>
            l.Action == AuditActionNames.NotificationMarkedRead &&
            l.EntityId == notificationId)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkAllRead_ClearsOnlyTheActorsUnreadItems()
    {
        var client = await SignInAsync("notif.it.bulk@netra.local", "Senior Officer");
        await SignInAsync("notif.it.foreign@netra.local", "Police / Intelligence Analyst");
        await SeedInboxAsync("notif.it.bulk@netra.local", "NOTIF-IT bulk one");
        await SeedInboxAsync("notif.it.bulk@netra.local", "NOTIF-IT bulk two");
        var (foreignId, _) = await SeedInboxAsync("notif.it.foreign@netra.local", "NOTIF-IT other user");

        var html = await client.GetStringAsync("/notifications");
        html.Should().Contain("2 unread notifications");

        var token = AuthenticationScreenTests.ExtractAntiForgeryToken(html);
        var response = await client.PostAsync("/notifications/read-all", AuthenticationScreenTests.Form(
            ("__RequestVerificationToken", token)));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var after = await client.GetStringAsync(response.Headers.Location!.ToString());
        after.Should().Contain("All notifications marked as read.");
        after.Should().Contain("0 unread");

        using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<NetraDbContext>();
        (await context.Notifications.FirstAsync(n => n.Id == foreignId)).IsRead.Should().BeFalse();
    }

    [Fact]
    public async Task UnreadOnly_FilterShowsUnreadItemsAndEmptyState()
    {
        var client = await SignInAsync("notif.it.unread@netra.local", "Police / Intelligence Analyst");
        await SeedInboxAsync("notif.it.unread@netra.local", "NOTIF-IT still unread");
        await SeedInboxAsync("notif.it.unread@netra.local", "NOTIF-IT already read", isRead: true);

        var all = await client.GetStringAsync("/notifications");
        var unread = await client.GetStringAsync("/notifications?unreadOnly=true");
        var fresh = await (await SignInAsync("notif.it.clear@netra.local", "Senior Officer"))
            .GetStringAsync("/notifications?unreadOnly=true");

        all.Should().Contain("NOTIF-IT still unread");
        all.Should().Contain("NOTIF-IT already read");
        unread.Should().Contain("NOTIF-IT still unread");
        unread.Should().NotContain("NOTIF-IT already read");
        fresh.Should().Contain("No unread notifications");
    }

    [Fact]
    public async Task Notifications_LinkedAlertNavigatesToTheAlertDetails()
    {
        var client = await SignInAsync("notif.it.linked@netra.local", "Senior Officer");
        var (_, alertId) = await SeedInboxAsync("notif.it.linked@netra.local", "NOTIF-IT linked item");

        var html = await client.GetStringAsync("/notifications");

        html.Should().Contain($"/alerts/details/{alertId}");
        html.Should().Contain("View alert");
        html.Should().Contain("Crime Intelligence Alert");
    }

    [Fact]
    public async Task MarkRead_WithoutAntiForgeryToken_IsRejected()
    {
        var client = await SignInAsync("notif.it.forgery@netra.local", "Investigator");
        var (notificationId, _) = await SeedInboxAsync("notif.it.forgery@netra.local", "NOTIF-IT forged item");

        var response = await client.PostAsync(
            $"/notifications/read/{notificationId}", AuthenticationScreenTests.Form(
                ("page", "1")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Notifications_PageEmitsNoJavaScript()
    {
        var client = await SignInAsync("notif.it.nojs@netra.local", "Police / Intelligence Analyst");
        await SeedInboxAsync("notif.it.nojs@netra.local", "NOTIF-IT no-js item");

        foreach (var path in new[] { "/notifications", "/notifications?unreadOnly=true" })
        {
            var html = await client.GetStringAsync(path);

            html.Should().NotContain("<script", $"{path} must stay script-free");
            html.Should().NotContain("javascript:");
            html.Should().NotContain("onclick");
        }
    }

    [Fact]
    public async Task Topbar_BellPointsAtTheNotificationCentre_AndSidebarLinksBothModules()
    {
        var client = await SignInAsync("notif.it.shell@netra.local", "Police / Intelligence Analyst");
        await SeedInboxAsync("notif.it.shell@netra.local", "NOTIF-IT shell item");

        var dashboard = await client.GetStringAsync("/Dashboard");
        var alerts = await client.GetStringAsync("/alerts");

        dashboard.Should().Contain("href=\"/notifications\"");
        dashboard.Should().Contain("1 unread notification");
        dashboard.Should().NotContain("/Placeholder");
        alerts.Should().Contain("href=\"/notifications\"");
    }
}
