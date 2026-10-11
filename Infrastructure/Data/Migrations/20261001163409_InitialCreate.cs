using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NETRA.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrimeTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ParentCrimeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrimeTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrimeTypes_CrimeTypes_ParentCrimeTypeId",
                        column: x => x.ParentCrimeTypeId,
                        principalTable: "CrimeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ParentDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Departments_ParentDepartmentId",
                        column: x => x.ParentDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "Districts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CenterLatitude = table.Column<double>(type: "float", nullable: true),
                    CenterLongitude = table.Column<double>(type: "float", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Districts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AlertType = table.Column<int>(type: "int", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AssignedToId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Anomalies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    AnomalyType = table.Column<int>(type: "int", nullable: false),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DetectedPatterns = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetectedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anomalies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BadgeNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Rank = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PoliceStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationUsers_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SessionType = table.Column<int>(type: "int", nullable: false),
                    ModelName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SystemPrompt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalMessages = table.Column<int>(type: "int", nullable: false),
                    TotalTokensUsed = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantSessions_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrimeCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CrimeTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    IncidentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReportedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AssignedInvestigatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupervisingOfficerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PoliceStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrimeCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrimeCases_ApplicationUsers_AssignedInvestigatorId",
                        column: x => x.AssignedInvestigatorId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CrimeCases_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrimeCases_ApplicationUsers_SupervisingOfficerId",
                        column: x => x.SupervisingOfficerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_CrimeCases_CrimeTypes_CrimeTypeId",
                        column: x => x.CrimeTypeId,
                        principalTable: "CrimeTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrimeCases_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ImportType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceFilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SourceFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    SourceFileContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TotalRecords = table.Column<int>(type: "int", nullable: false),
                    ProcessedRecords = table.Column<int>(type: "int", nullable: false),
                    SuccessfulRecords = table.Column<int>(type: "int", nullable: false),
                    FailedRecords = table.Column<int>(type: "int", nullable: false),
                    Configuration = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportJobs_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationType = table.Column<int>(type: "int", nullable: false),
                    DeliveryChannel = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Filters = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NotificationType = table.Column<int>(type: "int", nullable: false),
                    DeliveryChannel = table.Column<int>(type: "int", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSent = table.Column<bool>(type: "bit", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Alerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "Alerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Notifications_ApplicationUsers_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Gender = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    NationalIdNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PassportNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DriversLicenseNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Persons_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PoliceStations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    DistrictId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandingOfficerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PoliceStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PoliceStations_ApplicationUsers_CommandingOfficerId",
                        column: x => x.CommandingOfficerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PoliceStations_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecuritySettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystemSetting = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuritySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecuritySettings_ApplicationUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SystemPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValueType = table.Column<int>(type: "int", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemPreferences_ApplicationUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSettings_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssistantMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    TokensUsed = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistantMessages_AssistantSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AssistantSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PropertyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SessionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsSystemAction = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_ApplicationUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AuditLogs_CrimeCases_EntityId",
                        column: x => x.EntityId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "ImageRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileExtension = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ImageType = table.Column<int>(type: "int", nullable: false),
                    Metadata = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CameraInfo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageRecords_ApplicationUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImageRecords_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Investigations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeadInvestigatorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupervisingOfficerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Investigations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Investigations_ApplicationUsers_LeadInvestigatorId",
                        column: x => x.LeadInvestigatorId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Investigations_ApplicationUsers_SupervisingOfficerId",
                        column: x => x.SupervisingOfficerId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Investigations_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReportType = table.Column<int>(type: "int", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Parameters = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    GeneratedFilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GeneratedFileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    GeneratedFileContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneratedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reports_ApplicationUsers_GeneratedById",
                        column: x => x.GeneratedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Reports_ApplicationUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reports_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RiskAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Assessment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RiskLevel = table.Column<int>(type: "int", nullable: false),
                    RiskScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Factors = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Recommendations = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AssessedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssessedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskAssessments_ApplicationUsers_AssessedById",
                        column: x => x.AssessedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskAssessments_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportErrors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordNumber = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RawData = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportErrors_ApplicationUsers_ResolvedById",
                        column: x => x.ResolvedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportErrors_ImportJobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalTable: "ImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportJobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordNumber = table.Column<int>(type: "int", nullable: false),
                    RawData = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportRecords_ImportJobs_ImportJobId",
                        column: x => x.ImportJobId,
                        principalTable: "ImportJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CasePersons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AssociatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AssociatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CasePersons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CasePersons_ApplicationUsers_AssociatedById",
                        column: x => x.AssociatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CasePersons_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CasePersons_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AliasType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsPrimaryAlias = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonAliases_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonAliases_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Latitude = table.Column<double>(type: "float", nullable: true),
                    Longitude = table.Column<double>(type: "float", nullable: true),
                    PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DistrictId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PoliceStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Locations_Locations_ParentLocationId",
                        column: x => x.ParentLocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "FK_Locations_PoliceStations_PoliceStationId",
                        column: x => x.PoliceStationId,
                        principalTable: "PoliceStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImageMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MatchedImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SimilarityScore = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    MatchType = table.Column<int>(type: "int", nullable: false),
                    MatchDetails = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VerifiedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsFalsePositive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageMatches_ApplicationUsers_VerifiedById",
                        column: x => x.VerifiedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImageMatches_ImageRecords_MatchedImageId",
                        column: x => x.MatchedImageId,
                        principalTable: "ImageRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImageMatches_ImageRecords_SourceImageId",
                        column: x => x.SourceImageId,
                        principalTable: "ImageRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EvidenceType = table.Column<int>(type: "int", nullable: false),
                    EvidenceStatus = table.Column<int>(type: "int", nullable: false),
                    CollectedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CollectionLocation = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChainOfCustody = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CollectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evidence_ApplicationUsers_CollectedByUserId",
                        column: x => x.CollectedByUserId,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Evidence_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Evidence_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InvestigationNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    NoteType = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrivate = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationNotes_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestigationNotes_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TimelineEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvestigationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EventDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelineEvents_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TimelineEvents_Investigations_InvestigationId",
                        column: x => x.InvestigationId,
                        principalTable: "Investigations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CaseLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationRole = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AssociatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseLocations_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaseLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NetworkEntities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntitySubType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CrimeCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attributes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CentralityScore = table.Column<double>(type: "float(10)", precision: 10, scale: 4, nullable: true),
                    RiskLevel = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkEntities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NetworkEntities_CrimeCases_CrimeCaseId",
                        column: x => x.CrimeCaseId,
                        principalTable: "CrimeCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NetworkEntities_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NetworkEntities_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PersonLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ToDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonLocations_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileExtension = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvidenceFiles_ApplicationUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EvidenceFiles_Evidence_EvidenceId",
                        column: x => x.EvidenceId,
                        principalTable: "Evidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NetworkRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationshipType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Strength = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    EstablishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NetworkRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NetworkRelationships_ApplicationUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "ApplicationUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NetworkRelationships_NetworkEntities_SourceEntityId",
                        column: x => x.SourceEntityId,
                        principalTable: "NetworkEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NetworkRelationships_NetworkEntities_TargetEntityId",
                        column: x => x.TargetEntityId,
                        principalTable: "NetworkEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_AssignedToId",
                table: "Alerts",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CreatedAt",
                table: "Alerts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CrimeCaseId",
                table: "Alerts",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_RelatedEntityId",
                table: "Alerts",
                column: "RelatedEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_RelatedEntityType",
                table: "Alerts",
                column: "RelatedEntityType");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ResolvedById",
                table: "Alerts",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Severity",
                table: "Alerts",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Status",
                table: "Alerts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_DetectedAt",
                table: "Anomalies",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_DetectedById",
                table: "Anomalies",
                column: "DetectedById");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_RelatedEntityId",
                table: "Anomalies",
                column: "RelatedEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_RelatedEntityType",
                table: "Anomalies",
                column: "RelatedEntityType");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_ResolvedById",
                table: "Anomalies",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_Severity",
                table: "Anomalies",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_Anomalies_Status",
                table: "Anomalies",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "ApplicationUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_BadgeNumber",
                table: "ApplicationUsers",
                column: "BadgeNumber",
                unique: true,
                filter: "[BadgeNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_DepartmentId",
                table: "ApplicationUsers",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_IsActive",
                table: "ApplicationUsers",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationUsers_PoliceStationId",
                table: "ApplicationUsers",
                column: "PoliceStationId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "ApplicationUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_CreatedAt",
                table: "AssistantMessages",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_RelatedEntityId",
                table: "AssistantMessages",
                column: "RelatedEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_Role",
                table: "AssistantMessages",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantMessages_SessionId",
                table: "AssistantMessages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantSessions_StartedAt",
                table: "AssistantSessions",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantSessions_Status",
                table: "AssistantSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AssistantSessions_UserId",
                table: "AssistantSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CorrelationId",
                table: "AuditLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityId",
                table: "AuditLogs",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType",
                table: "AuditLogs",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_SessionId",
                table: "AuditLogs",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseLocations_CrimeCaseId",
                table: "CaseLocations",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CaseLocations_CrimeCaseId_LocationId",
                table: "CaseLocations",
                columns: new[] { "CrimeCaseId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaseLocations_LocationId",
                table: "CaseLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CasePersons_AssociatedById",
                table: "CasePersons",
                column: "AssociatedById");

            migrationBuilder.CreateIndex(
                name: "IX_CasePersons_CrimeCaseId",
                table: "CasePersons",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_CasePersons_CrimeCaseId_PersonId",
                table: "CasePersons",
                columns: new[] { "CrimeCaseId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CasePersons_PersonId",
                table: "CasePersons",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_CasePersons_Role",
                table: "CasePersons",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_AssignedInvestigatorId",
                table: "CrimeCases",
                column: "AssignedInvestigatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_CaseNumber",
                table: "CrimeCases",
                column: "CaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_CreatedById",
                table: "CrimeCases",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_CrimeTypeId",
                table: "CrimeCases",
                column: "CrimeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_DepartmentId",
                table: "CrimeCases",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_IncidentDate",
                table: "CrimeCases",
                column: "IncidentDate");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_PoliceStationId",
                table: "CrimeCases",
                column: "PoliceStationId");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_Severity",
                table: "CrimeCases",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_Status",
                table: "CrimeCases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeCases_SupervisingOfficerId",
                table: "CrimeCases",
                column: "SupervisingOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeTypes_Code",
                table: "CrimeTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrimeTypes_DisplayOrder",
                table: "CrimeTypes",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeTypes_IsActive",
                table: "CrimeTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CrimeTypes_ParentCrimeTypeId",
                table: "CrimeTypes",
                column: "ParentCrimeTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                table: "Departments",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_IsActive",
                table: "Departments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_ParentDepartmentId",
                table: "Departments",
                column: "ParentDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_CenterLatitude_CenterLongitude",
                table: "Districts",
                columns: new[] { "CenterLatitude", "CenterLongitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Districts_Code",
                table: "Districts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Districts_IsActive",
                table: "Districts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_CollectedByUserId",
                table: "Evidence",
                column: "CollectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_CreatedAt",
                table: "Evidence",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_EvidenceStatus",
                table: "Evidence",
                column: "EvidenceStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_EvidenceType",
                table: "Evidence",
                column: "EvidenceType");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_InvestigationId",
                table: "Evidence",
                column: "InvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_PersonId",
                table: "Evidence",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceFiles_ChecksumSha256",
                table: "EvidenceFiles",
                column: "ChecksumSha256");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceFiles_EvidenceId",
                table: "EvidenceFiles",
                column: "EvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceFiles_UploadedById",
                table: "EvidenceFiles",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMatches_IsVerified",
                table: "ImageMatches",
                column: "IsVerified");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMatches_MatchedImageId",
                table: "ImageMatches",
                column: "MatchedImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMatches_SimilarityScore",
                table: "ImageMatches",
                column: "SimilarityScore");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMatches_SourceImageId",
                table: "ImageMatches",
                column: "SourceImageId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageMatches_SourceImageId_MatchedImageId",
                table: "ImageMatches",
                columns: new[] { "SourceImageId", "MatchedImageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageMatches_VerifiedById",
                table: "ImageMatches",
                column: "VerifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRecords_ChecksumSha256",
                table: "ImageRecords",
                column: "ChecksumSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImageRecords_CrimeCaseId",
                table: "ImageRecords",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRecords_Latitude_Longitude",
                table: "ImageRecords",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageRecords_UploadedAt",
                table: "ImageRecords",
                column: "UploadedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRecords_UploadedById",
                table: "ImageRecords",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_ErrorCode",
                table: "ImportErrors",
                column: "ErrorCode");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_ImportJobId",
                table: "ImportErrors",
                column: "ImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_IsResolved",
                table: "ImportErrors",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_RecordNumber",
                table: "ImportErrors",
                column: "RecordNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_ResolvedById",
                table: "ImportErrors",
                column: "ResolvedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_CreatedById",
                table: "ImportJobs",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_StartedAt",
                table: "ImportJobs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_Status",
                table: "ImportJobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRecords_CreatedEntityId",
                table: "ImportRecords",
                column: "CreatedEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRecords_ImportJobId",
                table: "ImportRecords",
                column: "ImportJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRecords_RecordNumber",
                table: "ImportRecords",
                column: "RecordNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRecords_Status",
                table: "ImportRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationNotes_CreatedAt",
                table: "InvestigationNotes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationNotes_CreatedById",
                table: "InvestigationNotes",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationNotes_InvestigationId",
                table: "InvestigationNotes",
                column: "InvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_CrimeCaseId",
                table: "Investigations",
                column: "CrimeCaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_LeadInvestigatorId",
                table: "Investigations",
                column: "LeadInvestigatorId");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_Status",
                table: "Investigations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Investigations_SupervisingOfficerId",
                table: "Investigations",
                column: "SupervisingOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_DistrictId",
                table: "Locations",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_IsActive",
                table: "Locations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Latitude_Longitude",
                table: "Locations",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name",
                table: "Locations",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_ParentLocationId",
                table: "Locations",
                column: "ParentLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_PoliceStationId",
                table: "Locations",
                column: "PoliceStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Type",
                table: "Locations",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkEntities_CrimeCaseId",
                table: "NetworkEntities",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkEntities_EntityType",
                table: "NetworkEntities",
                column: "EntityType");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkEntities_IsActive",
                table: "NetworkEntities",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkEntities_LocationId",
                table: "NetworkEntities",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkEntities_PersonId",
                table: "NetworkEntities",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkEntities_RiskLevel",
                table: "NetworkEntities",
                column: "RiskLevel");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRelationships_CreatedById",
                table: "NetworkRelationships",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRelationships_IsActive",
                table: "NetworkRelationships",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRelationships_RelationshipType",
                table: "NetworkRelationships",
                column: "RelationshipType");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRelationships_SourceEntityId",
                table: "NetworkRelationships",
                column: "SourceEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRelationships_SourceEntityId_TargetEntityId_RelationshipType",
                table: "NetworkRelationships",
                columns: new[] { "SourceEntityId", "TargetEntityId", "RelationshipType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NetworkRelationships_TargetEntityId",
                table: "NetworkRelationships",
                column: "TargetEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_NotificationType",
                table: "NotificationPreferences",
                column: "NotificationType");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId",
                table: "NotificationPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId_NotificationType_DeliveryChannel",
                table: "NotificationPreferences",
                columns: new[] { "UserId", "NotificationType", "DeliveryChannel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_AlertId",
                table: "Notifications",
                column: "AlertId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CorrelationId",
                table: "Notifications",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAt",
                table: "Notifications",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsRead",
                table: "Notifications",
                column: "IsRead");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IsSent",
                table: "Notifications",
                column: "IsSent");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientId",
                table: "Notifications",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAliases_CreatedById",
                table: "PersonAliases",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAliases_IsPrimaryAlias",
                table: "PersonAliases",
                column: "IsPrimaryAlias");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAliases_PersonId",
                table: "PersonAliases",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonAliases_PersonId_IsPrimaryAlias",
                table: "PersonAliases",
                columns: new[] { "PersonId", "IsPrimaryAlias" },
                unique: true,
                filter: "[IsPrimaryAlias] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PersonLocations_LocationId",
                table: "PersonLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonLocations_PersonId",
                table: "PersonLocations",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonLocations_PersonId_LocationId_FromDate",
                table: "PersonLocations",
                columns: new[] { "PersonId", "LocationId", "FromDate" },
                unique: true,
                filter: "[FromDate] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_CreatedById",
                table: "Persons",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_Email",
                table: "Persons",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_FirstName",
                table: "Persons",
                column: "FirstName");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_IsActive",
                table: "Persons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_LastName",
                table: "Persons",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_NationalIdNumber",
                table: "Persons",
                column: "NationalIdNumber",
                unique: true,
                filter: "[NationalIdNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_PassportNumber",
                table: "Persons",
                column: "PassportNumber",
                unique: true,
                filter: "[PassportNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PoliceStations_Code",
                table: "PoliceStations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PoliceStations_CommandingOfficerId",
                table: "PoliceStations",
                column: "CommandingOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_PoliceStations_DistrictId",
                table: "PoliceStations",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_PoliceStations_IsActive",
                table: "PoliceStations",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PoliceStations_Latitude_Longitude",
                table: "PoliceStations",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.CreateIndex(
                name: "IX_Reports_CrimeCaseId",
                table: "Reports",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_GeneratedById",
                table: "Reports",
                column: "GeneratedById");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReportType",
                table: "Reports",
                column: "ReportType");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_RequestedAt",
                table: "Reports",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_RequestedById",
                table: "Reports",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_Status",
                table: "Reports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_AssessedAt",
                table: "RiskAssessments",
                column: "AssessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_AssessedById",
                table: "RiskAssessments",
                column: "AssessedById");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_CrimeCaseId",
                table: "RiskAssessments",
                column: "CrimeCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_RiskLevel",
                table: "RiskAssessments",
                column: "RiskLevel");

            migrationBuilder.CreateIndex(
                name: "IX_SecuritySettings_Category",
                table: "SecuritySettings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SecuritySettings_IsSystemSetting",
                table: "SecuritySettings",
                column: "IsSystemSetting");

            migrationBuilder.CreateIndex(
                name: "IX_SecuritySettings_Key",
                table: "SecuritySettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecuritySettings_UpdatedById",
                table: "SecuritySettings",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SystemPreferences_Category",
                table: "SystemPreferences",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_SystemPreferences_Key",
                table: "SystemPreferences",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemPreferences_UpdatedById",
                table: "SystemPreferences",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_CreatedById",
                table: "TimelineEvents",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_EventDateTime",
                table: "TimelineEvents",
                column: "EventDateTime");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_EventType",
                table: "TimelineEvents",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_InvestigationId",
                table: "TimelineEvents",
                column: "InvestigationId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEvents_RelatedEntityId",
                table: "TimelineEvents",
                column: "RelatedEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_Category",
                table: "UserSettings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_Key",
                table: "UserSettings",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId",
                table: "UserSettings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSettings_UserId_Key",
                table: "UserSettings",
                columns: new[] { "UserId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_ApplicationUsers_AssignedToId",
                table: "Alerts",
                column: "AssignedToId",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_ApplicationUsers_ResolvedById",
                table: "Alerts",
                column: "ResolvedById",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_Alerts_CrimeCases_CrimeCaseId",
                table: "Alerts",
                column: "CrimeCaseId",
                principalTable: "CrimeCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Anomalies_ApplicationUsers_DetectedById",
                table: "Anomalies",
                column: "DetectedById",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Anomalies_ApplicationUsers_ResolvedById",
                table: "Anomalies",
                column: "ResolvedById",
                principalTable: "ApplicationUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationUsers_PoliceStations_PoliceStationId",
                table: "ApplicationUsers",
                column: "PoliceStationId",
                principalTable: "PoliceStations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PoliceStations_ApplicationUsers_CommandingOfficerId",
                table: "PoliceStations");

            migrationBuilder.DropTable(
                name: "Anomalies");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AssistantMessages");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CaseLocations");

            migrationBuilder.DropTable(
                name: "CasePersons");

            migrationBuilder.DropTable(
                name: "EvidenceFiles");

            migrationBuilder.DropTable(
                name: "ImageMatches");

            migrationBuilder.DropTable(
                name: "ImportErrors");

            migrationBuilder.DropTable(
                name: "ImportRecords");

            migrationBuilder.DropTable(
                name: "InvestigationNotes");

            migrationBuilder.DropTable(
                name: "NetworkRelationships");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PersonAliases");

            migrationBuilder.DropTable(
                name: "PersonLocations");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "RiskAssessments");

            migrationBuilder.DropTable(
                name: "SecuritySettings");

            migrationBuilder.DropTable(
                name: "SystemPreferences");

            migrationBuilder.DropTable(
                name: "TimelineEvents");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AssistantSessions");

            migrationBuilder.DropTable(
                name: "Evidence");

            migrationBuilder.DropTable(
                name: "ImageRecords");

            migrationBuilder.DropTable(
                name: "ImportJobs");

            migrationBuilder.DropTable(
                name: "NetworkEntities");

            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "Investigations");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.DropTable(
                name: "CrimeCases");

            migrationBuilder.DropTable(
                name: "CrimeTypes");

            migrationBuilder.DropTable(
                name: "ApplicationUsers");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "PoliceStations");

            migrationBuilder.DropTable(
                name: "Districts");
        }
    }
}
