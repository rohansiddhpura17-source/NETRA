using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NETRA.Domain.AI;
using NETRA.Domain.Alerts;
using NETRA.Domain.Audit;
using NETRA.Domain.Cases;
using NETRA.Domain.Geography;
using NETRA.Domain.Import;
using NETRA.Domain.Intelligence;
using NETRA.Domain.Investigation;
using NETRA.Domain.People;
using NETRA.Domain.Reports;
using NETRA.Domain.Security;
using NETRA.Domain.Settings;
using NETRA.Domain.ImageInvestigation;
using NETRA.Infrastructure.Data.Configurations;

namespace NETRA.Infrastructure.Data;

public class NetraDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public NetraDbContext(DbContextOptions<NetraDbContext> options) : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<CrimeCase> CrimeCases => Set<CrimeCase>();
    public DbSet<CrimeType> CrimeTypes => Set<CrimeType>();
    public DbSet<CasePerson> CasePersons => Set<CasePerson>();
    public DbSet<CaseLocation> CaseLocations => Set<CaseLocation>();

    public DbSet<Person> Persons => Set<Person>();
    public DbSet<PersonAlias> PersonAliases => Set<PersonAlias>();
    public DbSet<PersonLocation> PersonLocations => Set<PersonLocation>();

    public DbSet<Location> Locations => Set<Location>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<PoliceStation> PoliceStations => Set<PoliceStation>();

    public DbSet<Investigation> Investigations => Set<Investigation>();
    public DbSet<InvestigationNote> InvestigationNotes => Set<InvestigationNote>();
    public DbSet<TimelineEvent> TimelineEvents => Set<TimelineEvent>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<EvidenceFile> EvidenceFiles => Set<EvidenceFile>();

    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();
    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<NetworkEntity> NetworkEntities => Set<NetworkEntity>();
    public DbSet<NetworkRelationship> NetworkRelationships => Set<NetworkRelationship>();

    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<ImageRecord> ImageRecords => Set<ImageRecord>();
    public DbSet<ImageMatch> ImageMatches => Set<ImageMatch>();

    public DbSet<Report> Reports => Set<Report>();

    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportRecord> ImportRecords => Set<ImportRecord>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<UserSetting> UserSettings => Set<UserSetting>();
    public DbSet<SecuritySetting> SecuritySettings => Set<SecuritySetting>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<SystemPreference> SystemPreferences => Set<SystemPreference>();

    public DbSet<AssistantSession> AssistantSessions => Set<AssistantSession>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(NetraDbContext).Assembly);

        builder.Entity<IdentityRole<Guid>>().ToTable("AspNetRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("AspNetUserClaims");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("AspNetUserRoles");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("AspNetUserLogins");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("AspNetRoleClaims");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("AspNetUserTokens");
    }
}