namespace Tahona.Domain.Enums;

public enum Gender { Male = 1, Female = 2 }

public enum PatientStatus { Active = 1, Inactive = 2, Archived = 3 }

public enum AppointmentStatus
{
    Scheduled = 1,
    Confirmed = 2,
    Arrived = 3,
    InTreatment = 4,
    Completed = 5,
    Cancelled = 6,
    NoShow = 7
}

public enum ToothConditionType
{
    Caries = 1,
    Filling = 2,
    Crown = 3,
    RootCanal = 4,
    Extraction = 5,
    Implant = 6,
    Missing = 7,
    Fracture = 8,
    Bridge = 9,
    Veneer = 10,
    Sealant = 11,
    Other = 99
}

public enum ConditionStatus { Diagnosed = 1, Planned = 2, Treated = 3, Resolved = 4 }

public enum ProcedureCategory
{
    Diagnostic = 1,
    Preventive = 2,
    Restorative = 3,
    Endodontic = 4,
    Prosthodontic = 5,
    OralSurgery = 6,
    Orthodontic = 7,
    Periodontal = 8,
    Cosmetic = 9,
    Implant = 10,
    Pediatric = 11
}

public enum TreatmentPlanStatus
{
    Draft = 1,
    Presented = 2,
    Accepted = 3,
    InProgress = 4,
    PartiallyCompleted = 5,
    Completed = 6,
    Rejected = 7
}

public enum TreatmentItemStatus { Planned = 1, Scheduled = 2, Completed = 3, Cancelled = 4 }

public enum InvoiceStatus { Draft = 1, Issued = 2, PartiallyPaid = 3, Paid = 4, Void = 5, Refunded = 6 }

public enum PaymentMethod { Cash = 1, Card = 2, BankTransfer = 3, CliQ = 4, Insurance = 5, Other = 99 }

public enum PaymentType { Payment = 1, Refund = 2 }

public enum ExpenseCategory { Rent = 1, Salaries = 2, Supplies = 3, Lab = 4, Equipment = 5, Utilities = 6, Marketing = 7, Maintenance = 8, Other = 99 }

/// <summary>The kinds of revenue leakage the Recovery Engine detects.</summary>
public enum RecoveryIssueType
{
    TreatmentNotStarted = 1,
    TreatmentUnfinished = 2,
    MissedAppointment = 3,
    OutstandingBalance = 4,
    OverdueFollowUp = 5,
    RecallDue = 6
}

public enum FollowUpTrigger
{
    MissedAppointment = 1,
    TreatmentPlanNotStarted = 2,
    TreatmentPlanInactive = 3,
    TreatmentUnfinished = 4,
    ProcedureCompleted = 5,
    RoutineRecall = 6,
    OverdueBalance = 7
}

public enum FollowUpStatus
{
    Pending = 1,
    Contacted = 2,
    Booked = 3,
    Recovered = 4,
    NoResponse = 5,
    Dismissed = 6,
    /// <summary>The underlying issue cleared without clinic outreach (not counted as recovered).</summary>
    Resolved = 7
}

public enum PriorityLevel { Low = 1, Medium = 2, High = 3, Urgent = 4 }

public enum MessageChannel { WhatsApp = 1, Sms = 2, Email = 3, Phone = 4 }

public enum MessageDirection { Outbound = 1, Inbound = 2 }

/// <summary>
/// Sent/Delivered/Read/Failed come from the WhatsApp provider. ManualPending = opened in WhatsApp for staff to send (not confirmed).
/// Logged = a phone call record. Received = an inbound patient message.
/// </summary>
public enum MessageStatus { Queued = 1, Sent = 2, Delivered = 3, Read = 4, Failed = 5, ManualPending = 6, Logged = 7, Received = 8 }

/// <summary>Whether the patient agreed to receive messages. Automated messages need Granted; OptedOut blocks every message.</summary>
public enum MessagingConsent { Unknown = 0, Granted = 1, OptedOut = 2 }

public enum MessageTemplateType
{
    AppointmentReminder = 1,
    TreatmentFollowUp = 2,
    MissedAppointment = 3,
    Recall = 4,
    PaymentReminder = 5,
    PostTreatment = 6,
    Custom = 99
}

public enum NotificationType
{
    NewAppointment = 1,
    UpcomingAppointment = 2,
    MissedAppointment = 3,
    OverduePayment = 4,
    TreatmentFollowUp = 5,
    HighValueInactiveTreatment = 6,
    NewPatient = 7,
    StaffAction = 8,
    System = 9,
    PatientReply = 10
}

/// <summary>
/// Commercial state of a clinic account. Trial, Active and PastDue have full access; Cancelled and Suspended are
/// read-only with export, so a clinic is never locked out of its own records.
/// </summary>
public enum SubscriptionStatus { Trial = 1, Active = 2, PastDue = 3, Cancelled = 4, Suspended = 5 }

public enum UserTokenPurpose { PasswordReset = 1 }

public enum ImportKind { Patients = 1, Appointments = 2, OpeningBalances = 3, TreatmentPlans = 4 }

/// <summary>Uploaded = parsed and waiting; Committed = records created; Undone = the created records were removed again.</summary>
public enum ImportBatchStatus { Uploaded = 1, Committed = 2, Undone = 3 }

public enum ImportRowStatus { Pending = 1, Created = 2, Updated = 3, Skipped = 4, Error = 5 }

public enum DocumentCategory { XRay = 1, Scan = 2, PatientDocument = 3, TreatmentImage = 4, Prescription = 5, Invoice = 6, Other = 99 }
