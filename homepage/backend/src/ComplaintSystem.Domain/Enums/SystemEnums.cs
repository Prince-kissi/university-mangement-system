namespace ComplaintSystem.Domain.Enums;

public enum AccountStatus { PendingVerification, Active, Suspended, Disabled, Locked, Deactivated }
public enum PriorityLevel { Low, Normal, High, Urgent, Critical }
public enum ComplaintStatus
{
    Submitted, Unassigned, Assigned, Acknowledged, UnderReview, AwaitingStudentResponse,
    InvestigationInProgress, Escalated, Resolved, Closed, Rejected, Reopened
}
public enum ResponseVisibility { Public, Internal }
public enum EvidenceAccessType { View, Download }
public enum NotificationType { Information, ComplaintUpdate, Announcement, Security }
public enum BackupStatus { Requested, Running, Completed, Failed, Restored }
public enum MalwareScanStatus { Pending, Clean, Infected, Failed }
