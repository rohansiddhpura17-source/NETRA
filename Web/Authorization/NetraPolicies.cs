namespace NETRA.Web.Authorization;

/// <summary>
/// Authorisation policy names. Referenced by attributes and by the policy
/// registration in <c>Program</c>, so a typo fails at startup rather than silently
/// leaving an action unprotected.
/// </summary>
public static class NetraPolicies
{
    /// <summary>May read cases and investigations.</summary>
    public const string CaseRead = "netra.case.read";

    /// <summary>May register a new case.</summary>
    public const string CaseCreate = "netra.case.create";

    /// <summary>May change core case fields (status, severity, crime type, dates).</summary>
    public const string CaseEdit = "netra.case.edit";

    /// <summary>May change who is assigned to a case.</summary>
    public const string CaseAssign = "netra.case.assign";

    /// <summary>May add or remove people on a case.</summary>
    public const string CaseManagePersons = "netra.case.persons";

    /// <summary>May add or remove locations on a case.</summary>
    public const string CaseManageLocations = "netra.case.locations";

    /// <summary>May add evidence and upload files.</summary>
    public const string CaseManageEvidence = "netra.case.evidence";

    /// <summary>May add investigation notes.</summary>
    public const string CaseManageNotes = "netra.case.notes";

    /// <summary>May add timeline events.</summary>
    public const string CaseManageTimeline = "netra.case.timeline";

    /// <summary>May remove investigation notes or timeline entries.</summary>
    public const string CaseManageRecords = "netra.case.records";

    /// <summary>Audit log access.</summary>
    public const string AuditRead = "netra.audit.read";

    /// <summary>Crime analytics access.</summary>
    public const string AnalyticsRead = "netra.analytics.read";

    /// <summary>
    /// Network and link analysis access. This is deliberately restricted to the
    /// analytical roles that can already read case records; Investigators retain
    /// normal case access but do not receive cross-record network analysis.
    /// </summary>
    public const string NetworkRead = "netra.network.read";

    /// <summary>Risk &amp; anomaly analysis access (analytical indicators, decision support).</summary>
    public const string RiskRead = "netra.risk.read";

    /// <summary>
    /// Image-assisted investigation access. This is an operational investigative
    /// capability (upload + synthetic candidate leads), so it includes Investigators
    /// but not every authenticated user.
    /// </summary>
    public const string ImageInvestigation = "netra.imageinvestigation";

    /// <summary>
    /// AI Assistant access. The assistant answers analytics, district, risk and
    /// person-association questions, so it follows the analytical reader group
    /// (administrators, senior officers, analysts) rather than opening the whole
    /// data set to every authenticated user.
    /// </summary>
    public const string Assistant = "netra.assistant";

    /// <summary>
    /// Alert review and triage. Alerts are derived from analytics and case
    /// relationships, and Investigator needs to acknowledge and resolve the
    /// operational leads raised against cases it works.
    /// </summary>
    public const string AlertsRead = "netra.alerts.read";

    /// <summary>
    /// Analytic alert generation and triage transitions. Limited to the
    /// supervisory and analytical roles that own risk decisions.
    /// </summary>
    public const string AlertsManage = "netra.alerts.manage";

    /// <summary>
    /// Report list and document access. Case summaries and investigation summaries are
    /// operational documents about work an Investigator already performs, so the read
    /// policy covers every case-working role.
    /// </summary>
    public const string ReportsRead = "netra.reports.read";

    /// <summary>
    /// Access to reports whose content is analytical — risk assessments, crime trend
    /// analysis and executive intelligence summaries. Deliberately narrower than
    /// <see cref="ReportsRead"/>: a report can carry risk indicators that its reader is
    /// not authorised to see, so report content (not just the route) drives the gate.
    /// </summary>
    public const string ReportsAnalysis = "netra.reports.analysis";

    /// <summary>Generate, author, edit and archive reports.</summary>
    public const string ReportsManage = "netra.reports.manage";

    /// <summary>
    /// Bulk data import: uploading, validating, previewing and confirming case CSV imports.
    /// Import writes case records in volume and is therefore a supervised, administrative
    /// capability rather than everyday casework, so Investigators do not hold it.
    /// </summary>
    public const string BulkImportManage = "netra.bulkimport.manage";

    /// <summary>
    /// User Management: listing, creating, editing, assigning roles to and activating or
    /// deactivating accounts. This is the ability to grant access, so it is held only by
    /// the administrative role and is enforced server-side on every action.
    /// </summary>
    public const string UserManagementManage = "netra.user.management";

    /// <summary>System administration.</summary>
    public const string Administration = "netra.administration";
}

/// <summary>Role name constants, mirroring <c>IdentitySeedService</c>.</summary>
public static class NetraRoles
{
    public const string SystemAdministrator = "System Administrator";
    public const string PoliceIntelligenceAnalyst = "Police / Intelligence Analyst";
    public const string Investigator = "Investigator";
    public const string SeniorOfficer = "Senior Officer";

    public static readonly string[] All =
    [
        SystemAdministrator,
        PoliceIntelligenceAnalyst,
        Investigator,
        SeniorOfficer
    ];

    /// <summary>Roles permitted to work case records.</summary>
    public static readonly string[] CaseWorkers =
    [
        SystemAdministrator,
        PoliceIntelligenceAnalyst,
        Investigator,
        SeniorOfficer
    ];

    /// <summary>Roles permitted to change core case fields and associations.</summary>
    public static readonly string[] CaseEditors =
    [
        SystemAdministrator,
        PoliceIntelligenceAnalyst,
        Investigator,
        SeniorOfficer
    ];

    /// <summary>Roles permitted to remove records such as notes and timeline entries.</summary>
    public static readonly string[] RecordSupervisors =
    [
        SystemAdministrator,
        SeniorOfficer
    ];

    /// <summary>Roles permitted to assign investigators.</summary>
    public static readonly string[] Assigners =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to read the audit log.</summary>
    public static readonly string[] AuditReaders =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to read crime analytics.</summary>
    public static readonly string[] AnalyticsReaders =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles authorised to investigate cross-record relationships.</summary>
    public static readonly string[] NetworkAnalysts =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to read risk and anomaly analysis.</summary>
    public static readonly string[] RiskAnalysts =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to run image-assisted investigations.</summary>
    public static readonly string[] ImageInvestigators =
    [
        SystemAdministrator,
        Investigator,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to use the AI Assistant (same analytical group as risk/analytics).</summary>
    public static readonly string[] AssistantReaders =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to view and triage alerts.</summary>
    public static readonly string[] AlertViewers =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst,
        Investigator
    ];

    /// <summary>Roles permitted to run analytic alert generation.</summary>
    public static readonly string[] AlertManagers =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to open reports about their own casework.</summary>
    public static readonly string[] ReportViewers =
    [
        SystemAdministrator,
        PoliceIntelligenceAnalyst,
        Investigator,
        SeniorOfficer
    ];

    /// <summary>Roles permitted to read analytical report content (same group as risk/analytics).</summary>
    public static readonly string[] ReportAnalysts =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to generate, author and archive reports.</summary>
    public static readonly string[] ReportManagers =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    /// <summary>Roles permitted to run bulk case imports end to end.</summary>
    public static readonly string[] BulkImportManagers =
    [
        SystemAdministrator,
        SeniorOfficer,
        PoliceIntelligenceAnalyst
    ];

    public static readonly string[] Administrators =
    [
        SystemAdministrator
    ];
}
