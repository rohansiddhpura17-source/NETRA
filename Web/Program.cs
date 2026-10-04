using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NETRA.Application.Abstractions;
using NETRA.Application.AI;
using NETRA.Application.Alerts;
using NETRA.Application.Analytics;
using NETRA.Application.Audit;
using NETRA.Application.BulkImport;
using NETRA.Application.CaseOperations;
using NETRA.Application.Dashboard;
using NETRA.Application.ImageInvestigation;
using NETRA.Application.Maps;
using NETRA.Application.Network;
using NETRA.Application.Reports;
using NETRA.Application.Risk;
using NETRA.Application.Settings;
using NETRA.Application.Users;
using NETRA.Domain.Security;
using NETRA.Infrastructure.AI;
using NETRA.Infrastructure.Data;
using NETRA.Infrastructure.Identity;
using NETRA.Infrastructure.Services;
using NETRA.Web.Authorization;
using NETRA.Web.Filters;
using NETRA.Web.Services;
using NETRA.Web.Services.Charts;
using NETRA.Web.Services.Maps;
using NETRA.Web.Services.Network;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<NetraDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<NetraDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/error/403";

    // The cookie is always HttpOnly and SameSite=Lax; only the Secure flag has to be
    // conditional, because the development server runs on plain HTTP and forcing Secure
    // there would make the app unusable locally. Any non-Development environment — including
    // a deployment behind a TLS-terminating proxy — gets a cookie that a browser will never
    // send over an insecure connection.
    if (!builder.Environment.IsDevelopment())
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }

    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;

    // By default the cookie handler redirects forbidden users (302). Return a real 403
    // instead so status-code pages render the styled NETRA error view with a correct
    // status line for monitoring and crawlers.
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Live verification of a Production HTTPS endpoint showed the framework issuing the
// anti-forgery cookie HttpOnly and SameSite=Strict but without the Secure attribute.
// Outside Development it is forced here, so a token cookie can never travel over plain HTTP
// or be read by page script. Development keeps plain HTTP usable.
builder.Services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(options =>
{
    options.Cookie.HttpOnly = true;

    if (!builder.Environment.IsDevelopment())
    {
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    }
});

// TLS is commonly terminated by a reverse proxy, which leaves Kestrel receiving plain HTTP.
// Outside Development both the Identity and the anti-forgery cookies are marked Secure, and
// ASP.NET Core refuses to mint an anti-forgery token for a request it does not consider HTTPS,
// so every form page would fail with an unhandled error. Forwarded headers restore the real
// scheme — but only from proxies the operator names explicitly, because a header any client
// can send would let a plain HTTP request claim to be secure. With no entry configured the
// middleware reads no forwarded header at all.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    var proxies = builder.Configuration.GetSection("Security:TrustedProxies").Get<string[]>() ?? [];

    foreach (var entry in proxies)
    {
        // An entry is an exact proxy address (203.0.113.4) or a CIDR block (10.0.0.0/8).
        if (entry.Contains('/'))
        {
            if (!System.Net.IPNetwork.TryParse(entry, out var network))
            {
                throw new InvalidOperationException(
                    $"Security:TrustedProxies contains '{entry}', which is not a CIDR block.");
            }

            options.KnownIPNetworks.Add(network);
        }
        else if (System.Net.IPAddress.TryParse(entry, out var address))
        {
            options.KnownProxies.Add(address);
        }
        else
        {
            throw new InvalidOperationException(
                $"Security:TrustedProxies contains '{entry}', which is not an IP address.");
        }
    }

    if (proxies.Length > 0)
    {
        options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                                   | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    }
});

builder.Services.AddScoped<IdentitySeedService>();
builder.Services.AddScoped<DemoCaseDataSeeder>();
builder.Services.AddScoped<INetraUserContext, NetraUserContext>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

// Application services: controllers depend on these abstractions, never on EF Core.
builder.Services.AddScoped<ICaseService, CaseService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
builder.Services.AddScoped<IRiskAnalysisService, RiskAnalysisService>();
builder.Services.AddScoped<IMapService, MapService>();
builder.Services.AddScoped<INetworkAnalysisService, NetworkAnalysisService>();
builder.Services.AddScoped<IImageMatchProvider, MockImageMatchProvider>();
builder.Services.AddScoped<IImageInvestigationService, ImageInvestigationService>();

// AI Assistant: providers only phrase results the approved query layer already
// produced. MockAiService is the deterministic offline default and the mandatory
// fallback when a local model server is configured but unreachable.
builder.Services.AddSingleton<MockAiService>();
builder.Services.AddHttpClient<LocalAiService>((sp, client) =>
{
    var aiOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.TimeoutSeconds, 1, 120));
});
builder.Services.AddScoped<IAiService>(sp =>
{
    var aiOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
    return string.Equals(aiOptions.Provider, "Local", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<LocalAiService>()
        : sp.GetRequiredService<MockAiService>();
});
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddScoped<IAssistantService, AssistantService>();

// Alerts & notifications (Phase 10). Generation is explicit and idempotent;
// notification queries always scope to the signed-in recipient.
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Reports & intelligence summaries (Phase 11). The service assembles documents from
// the services above; it holds no analytics or risk logic of its own.
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuditActorAccessor, HttpContextAuditActorAccessor>();

// Bulk data import (Phase 12). CSV parsing, staging and the confirmed import all live in
// the service; the controller only maps form input and renders the workflow pages.
builder.Services.AddScoped<IBulkImportService, BulkImportService>();

// User management and the audit-log reader (Phase 13). Account writes go through
// UserManager/RoleManager; the audit reader only selects from the existing AuditLogs
// table, so no second user or audit store exists anywhere in the application.
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IAuditLogQueryService, AuditLogQueryService>();

// Settings and support (Phase 14). Personal settings reuse the existing Identity account
// and the existing UserSettings / NotificationPreferences tables; the layout reads the
// signed-in user's stored display choices so the theme can be applied server-side.
builder.Services.AddScoped<ISettingsService, SettingsService>();

// Server-side SVG chart renderers: deterministic markup, no client-side JavaScript.
builder.Services.AddSingleton<IChartRenderer, SvgLineChartRenderer>();
builder.Services.AddSingleton<IChartRenderer, SvgBarChartRenderer>();
builder.Services.AddSingleton<IChartRenderer, SvgDonutChartRenderer>();
builder.Services.AddSingleton<ChartRendererRegistry>();
builder.Services.AddSingleton<SvgSparklineRenderer>();

// Server-side SVG crime map: schematic geographic projection, no JavaScript map library.
builder.Services.AddSingleton<IMapRenderer, SvgMapRenderer>();
builder.Services.AddSingleton<INetworkGraphRenderer, NetworkGraphRenderer>();
builder.Services.AddSingleton<IFileStorageService>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileStorageOptions>>();
    var environment = sp.GetRequiredService<IHostEnvironment>();
    return new FileStorageService(options, environment.ContentRootPath);
});

builder.Services.Configure<FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));

// Authorisation policies. Registering them explicitly means a policy referenced by an
// attribute but never registered fails at startup instead of silently allowing access.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(NetraPolicies.CaseRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseWorkers));

    options.AddPolicy(NetraPolicies.CaseCreate, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseWorkers));

    options.AddPolicy(NetraPolicies.CaseEdit, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseEditors));

    options.AddPolicy(NetraPolicies.CaseAssign, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.Assigners));

    options.AddPolicy(NetraPolicies.CaseManagePersons, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseEditors));

    options.AddPolicy(NetraPolicies.CaseManageLocations, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseEditors));

    options.AddPolicy(NetraPolicies.CaseManageEvidence, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseWorkers));

    options.AddPolicy(NetraPolicies.CaseManageNotes, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseWorkers));

    options.AddPolicy(NetraPolicies.CaseManageTimeline, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.CaseWorkers));

    options.AddPolicy(NetraPolicies.CaseManageRecords, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.RecordSupervisors));

    options.AddPolicy(NetraPolicies.AuditRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.AuditReaders));

    options.AddPolicy(NetraPolicies.AnalyticsRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.AnalyticsReaders));

    options.AddPolicy(NetraPolicies.NetworkRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.NetworkAnalysts));

    options.AddPolicy(NetraPolicies.RiskRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.RiskAnalysts));

    options.AddPolicy(NetraPolicies.ImageInvestigation, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.ImageInvestigators));

    options.AddPolicy(NetraPolicies.Assistant, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.AssistantReaders));

    options.AddPolicy(NetraPolicies.AlertsRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.AlertViewers));

    options.AddPolicy(NetraPolicies.AlertsManage, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.AlertManagers));

    // Reports: read access covers operational case documents; analytical report content
    // (risk, trend, executive intelligence) is gated separately from the route policy.
    options.AddPolicy(NetraPolicies.ReportsRead, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.ReportViewers));

    options.AddPolicy(NetraPolicies.ReportsAnalysis, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.ReportAnalysts));

    options.AddPolicy(NetraPolicies.ReportsManage, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.ReportManagers));

    // Bulk import writes case records in volume, so it is gated on its own policy rather
    // than on the report or case-edit policies an operator may also hold.
    options.AddPolicy(NetraPolicies.BulkImportManage, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.BulkImportManagers));

    options.AddPolicy(NetraPolicies.UserManagementManage, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.Administrators));

    options.AddPolicy(NetraPolicies.Administration, policy =>
        policy.RequireAuthenticatedUser().RequireRole(NetraRoles.Administrators));
});

builder.Services.AddControllersWithViews(options =>
{
    // Populates CurrentUser / navigation data for the NETRA shell.
    options.Filters.Add<NetraShellFilter>();

    // A global AutoValidateAntiforgeryTokenAttribute was evaluated here and deliberately
    // NOT used: it makes the antiforgery check run before endpoint authorization, so an
    // unauthorized POST would answer 400 instead of 403 and the authorization contract the
    // tests pin would silently change. Every state-changing action therefore carries its own
    // [ValidateAntiForgeryToken], and the test suite asserts the token refusal per route.
});

var app = builder.Build();

// Database initialisation is opt-in so the UI can be developed and reviewed without a
// live SQL Server instance. Failures are never swallowed: when initialisation is enabled
// and the database is unreachable, startup fails loudly.
//
//   Database:AutoMigrate = true   (default in Development)
//
var autoMigrate = builder.Configuration.GetValue("Database:AutoMigrate", app.Environment.IsDevelopment());

if (autoMigrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    var services = scope.ServiceProvider;

    // No try/catch: a genuine database failure must stop startup, not be hidden.
    var context = services.GetRequiredService<NetraDbContext>();
    await context.Database.MigrateAsync();

    var seedService = services.GetRequiredService<IdentitySeedService>();
    await seedService.SeedAsync();
}
else
{
    app.Logger.LogInformation(
        "Database:AutoMigrate is disabled; skipping migrations and seeding. " +
        "The UI will run against an existing database. Apply migrations manually with " +
        "'dotnet ef database update' when required.");
}

// Must run before anything that depends on the request scheme: HSTS, HTTPS redirection and
// the cookies issued for the pages below. With no trusted proxies configured this reads
// nothing, so a directly exposed instance keeps using the transport Kestrel actually sees.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

// Response headers NETRA should never have needed to set because it has no client-side
// application code — `script-src 'none'` and `connect-src 'none'` make that a browser
// enforcement rather than a convention, so a script injected into stored data cannot run
// even if a server-side check were ever bypassed. `style-src` keeps 'unsafe-inline' because
// the server-rendered views carry inline style attributes for computed widths and SVG
// geometry; there are no <style> blocks and no external stylesheets beyond the site CSS.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'none'; " +
        "connect-src 'none'; " +
        "img-src 'self' data:; " +
        "style-src 'self' 'unsafe-inline'; " +
        "font-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>
/// Exposed so integration tests can boot the application with
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program
{
}
