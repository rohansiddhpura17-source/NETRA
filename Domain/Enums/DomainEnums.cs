namespace NETRA.Domain.Enums;

public enum CaseStatus
{
    Open = 1,
    UnderInvestigation = 2,
    PendingReview = 3,
    Closed = 4,
    Archived = 5,
    Reopened = 6
}

public enum CaseSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum PersonRole
{
    Suspect = 1,
    Victim = 2,
    Witness = 3,
    PersonOfInterest = 4,
    Investigator = 5,
    Officer = 6,
    Informant = 7,
    LegalCounsel = 8
}

public enum AlertSeverity
{
    Info = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Critical = 5
}

public enum AlertStatus
{
    New = 1,
    Acknowledged = 2,
    InProgress = 3,
    Resolved = 4,
    Dismissed = 5,
    Escalated = 6
}

/// <summary>
/// Controlled alert categories stored in <c>Alert.AlertType</c>. Generated alerts
/// always carry one of these values; arbitrary strings are never used.
/// </summary>
public enum AlertCategory
{
    CrimeSpike = 1,
    UnusualPattern = 2,
    HighRiskArea = 3,
    RelatedCases = 4,
    SecuritySystem = 5
}

/// <summary>
/// Controlled notification categories stored in <c>Notification.NotificationType</c>
/// and <c>NotificationPreference.NotificationType</c>.
/// </summary>
public enum NotificationCategory
{
    CrimeIntelligence = 1,
    Investigation = 2,
    System = 3
}

public enum RelationshipType
{
    Associate = 1,
    Family = 2,
    Colleague = 3,
    WitnessTo = 4,
    SuspectOf = 5,
    VictimOf = 6,
    ReportsTo = 7,
    Supervises = 8,
    KnownAssociate = 9
}

public enum ReportType
{
    CaseSummary = 1,
    InvestigationProgress = 2,
    EvidenceInventory = 3,
    IntelligenceBrief = 4,
    RiskAssessment = 5,
    StatisticalAnalysis = 6,
    AuditTrail = 7,
    Custom = 8
}

/// <summary>Who produced a report. System facts are immutable; authored content is not.</summary>
public enum ReportSource
{
    SystemGenerated = 1,
    AnalystAuthored = 2
}

public enum ReportStatus
{
    Draft = 1,
    Ready = 2,
    Archived = 3
}

/// <summary>
/// Lifecycle of a bulk import job. The staging states are distinct because a job must
/// never be importable before it has been validated, and re-running an import has to be
/// detectable: <see cref="Validated"/> means "parsed, checked and waiting for an explicit
/// confirmation", and any state after it proves the file already went through the gate.
/// </summary>
public enum ImportStatus
{
    /// <summary>File uploaded and stored. Contents not parsed yet.</summary>
    Pending = 1,

    /// <summary>Import is running.</summary>
    Processing = 2,

    /// <summary>Import finished with every staged row written.</summary>
    Completed = 3,

    /// <summary>Import finished, but rows were rejected or skipped.</summary>
    CompletedWithErrors = 4,

    /// <summary>Validation or the import itself failed; nothing was written by an import.</summary>
    Failed = 5,

    /// <summary>Cancelled by the operator before importing.</summary>
    Cancelled = 6,

    /// <summary>Parsed and validated. Awaiting an explicit import confirmation.</summary>
    Validated = 7
}

/// <summary>What a job imports. Only crime cases are supported.</summary>
public enum ImportType
{
    CrimeCases = 1
}

/// <summary>Per-row outcome stored in <c>ImportRecord.Status</c>.</summary>
public enum ImportRecordStatus
{
    /// <summary>Row read, not classified yet.</summary>
    Pending = 1,

    /// <summary>Row passed every rule and is ready to import.</summary>
    Valid = 2,

    /// <summary>Row broke at least one rule and will be rejected.</summary>
    Invalid = 3,

    /// <summary>Row repeats a case number and will be skipped.</summary>
    Duplicate = 4,

    /// <summary>Row was written to the case register by the import.</summary>
    Imported = 5
}

/// <summary>How a recorded import issue affects the row. Stored in <c>ImportError.Severity</c>.</summary>
public enum ImportIssueSeverity
{
    /// <summary>Blocks the row from importing.</summary>
    Error = 1,

    /// <summary>Row still imports, but the operator should know.</summary>
    Warning = 2
}

public enum AnomalySeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}