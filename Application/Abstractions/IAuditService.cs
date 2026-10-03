namespace NETRA.Application.Abstractions;

/// <summary>
/// Centralised audit trail writer. Controllers must not write audit records directly;
/// they call services, and services call this abstraction.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Queues an audit entry for the given entity. Entries are written on the next
    /// <c>SaveChangesAsync</c> of the ambient unit of work.
    /// </summary>
    void Record(
        string entityType,
        Guid entityId,
        string action,
        Guid userId,
        string? propertyName = null,
        string? oldValue = null,
        string? newValue = null,
        string? correlationId = null,
        string? ipAddress = null,
        string? userAgent = null);

    /// <summary>Queues a security-relevant event such as a sign-in attempt.</summary>
    void RecordSecurityEvent(
        string action,
        Guid userId,
        string? detail = null,
        bool succeeded = true,
        string? correlationId = null,
        string? ipAddress = null);
}

/// <summary>Well-known entity type names and action names for audit records.</summary>
public static class AuditEntityTypeNames
{
    public const string CrimeCase = "CrimeCase";
    public const string CasePerson = "CasePerson";
    public const string CaseLocation = "CaseLocation";
    public const string Evidence = "Evidence";
    public const string EvidenceFile = "EvidenceFile";
    public const string InvestigationNote = "InvestigationNote";
    public const string TimelineEvent = "TimelineEvent";
    public const string Account = "Account";
    public const string RiskAssessment = "RiskAssessment";
    public const string Anomaly = "Anomaly";
    public const string ImageRecord = "ImageRecord";
    public const string ImageMatch = "ImageMatch";
    public const string AssistantSession = "AssistantSession";
    public const string Alert = "Alert";
    public const string Notification = "Notification";
    public const string Report = "Report";
    public const string ImportJob = "ImportJob";
}

public static class AuditActionNames
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string InvestigatorAssigned = "InvestigatorAssigned";
    public const string PersonAssociated = "PersonAssociated";
    public const string PersonDisassociated = "PersonDisassociated";
    public const string LocationAssociated = "LocationAssociated";
    public const string LocationDisassociated = "LocationDisassociated";
    public const string EvidenceUploaded = "EvidenceUploaded";
    public const string NoteCreated = "NoteCreated";
    public const string TimelineEventCreated = "TimelineEventCreated";
    public const string ImageAnalysisRequested = "ImageAnalysisRequested";
    public const string ImageResultsViewed = "ImageResultsViewed";
    public const string ImageMatchViewed = "ImageMatchViewed";
    public const string AssistantSessionCreated = "AssistantSessionCreated";
    public const string AssistantQuestionSubmitted = "AssistantQuestionSubmitted";
    public const string AssistantQueryExecuted = "AssistantQueryExecuted";
    public const string AssistantResponseGenerated = "AssistantResponseGenerated";
    public const string AssistantProviderFailure = "AssistantProviderFailure";
    public const string AssistantAccessDenied = "AssistantAccessDenied";
    public const string AlertGenerated = "AlertGenerated";
    public const string AlertViewed = "AlertViewed";
    public const string AlertAcknowledged = "AlertAcknowledged";
    public const string AlertResolved = "AlertResolved";
    public const string AlertDismissed = "AlertDismissed";
    public const string NotificationCreated = "NotificationCreated";
    public const string NotificationMarkedRead = "NotificationMarkedRead";
    public const string NotificationAccessDenied = "NotificationAccessDenied";
    public const string ReportCreated = "ReportCreated";
    public const string ReportGenerated = "ReportGenerated";
    public const string ReportViewed = "ReportViewed";
    public const string ReportEdited = "ReportEdited";
    public const string ReportArchived = "ReportArchived";
    public const string ReportExported = "ReportExported";
    public const string ReportAccessDenied = "ReportAccessDenied";

    /// <summary>A staged file was accepted and stored. Never carries file contents.</summary>
    public const string ImportUploaded = "ImportUploaded";

    public const string ImportValidated = "ImportValidated";

    /// <summary>The operator looked at a preview. Nothing was written by this action.</summary>
    public const string ImportPreviewed = "ImportPreviewed";

    public const string ImportStarted = "ImportStarted";

    public const string ImportCompleted = "ImportCompleted";

    public const string ImportFailed = "ImportFailed";

    public const string ImportCancelled = "ImportCancelled";

    /// <summary>A job belonging to another operator was refused.</summary>
    public const string ImportAccessDenied = "ImportAccessDenied";

    // Account administration. The entity type is always AuditEntityTypeNames.Account
    // and the entity id is the user id, matching the sign-in events below.
    public const string UserCreated = "UserCreated";
    public const string UserEdited = "UserEdited";

    /// <summary>Written only when a role assignment actually changed.</summary>
    public const string UserRoleChanged = "UserRoleChanged";

    public const string UserActivated = "UserActivated";
    public const string UserDeactivated = "UserDeactivated";

    /// <summary>A destructive administrative change was refused. Nothing was written.</summary>
    public const string UserAccessDenied = "UserAccessDenied";
    public const string SignInSucceeded = "SignInSucceeded";
    public const string SignInFailed = "SignInFailed";
    public const string SignOut = "SignOut";

    // Self-service settings (Phase 14). The entity type is always
    // AuditEntityTypeNames.Account and the entity id is the owner's user id, which is
    // also the actor: these records describe an account changing its own settings.
    // PasswordChanged carries no password material — only the fact that it changed.
    public const string PasswordChanged = "PasswordChanged";
    public const string NotificationPreferencesUpdated = "NotificationPreferencesUpdated";
    public const string AppearancePreferencesUpdated = "AppearancePreferencesUpdated";
}