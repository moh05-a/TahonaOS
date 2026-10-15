// API contract types (mirrors the ASP.NET Core DTOs; enums are serialized as strings).

export type Gender = 'Male' | 'Female'
export type PatientStatus = 'Active' | 'Inactive' | 'Archived'
export type AppointmentStatus = 'Scheduled' | 'Confirmed' | 'Arrived' | 'InTreatment' | 'Completed' | 'Cancelled' | 'NoShow'
export type ToothConditionType =
  | 'Caries' | 'Filling' | 'Crown' | 'RootCanal' | 'Extraction' | 'Implant' | 'Missing' | 'Fracture' | 'Bridge' | 'Veneer' | 'Sealant' | 'Other'
export type ConditionStatus = 'Diagnosed' | 'Planned' | 'Treated' | 'Resolved'
export type ProcedureCategory =
  | 'Diagnostic' | 'Preventive' | 'Restorative' | 'Endodontic' | 'Prosthodontic' | 'OralSurgery' | 'Orthodontic' | 'Periodontal'
  | 'Cosmetic' | 'Implant' | 'Pediatric'
export type TreatmentPlanStatus = 'Draft' | 'Presented' | 'Accepted' | 'InProgress' | 'PartiallyCompleted' | 'Completed' | 'Rejected'
export type TreatmentItemStatus = 'Planned' | 'Scheduled' | 'Completed' | 'Cancelled'
export type InvoiceStatus = 'Draft' | 'Issued' | 'PartiallyPaid' | 'Paid' | 'Void' | 'Refunded'
export type PaymentMethod = 'Cash' | 'Card' | 'BankTransfer' | 'CliQ' | 'Insurance' | 'Other'
export type PaymentType = 'Payment' | 'Refund'
export type ExpenseCategory = 'Rent' | 'Salaries' | 'Supplies' | 'Lab' | 'Equipment' | 'Utilities' | 'Marketing' | 'Maintenance' | 'Other'
export type RecoveryIssueType =
  | 'TreatmentNotStarted' | 'TreatmentUnfinished' | 'MissedAppointment' | 'OutstandingBalance' | 'OverdueFollowUp' | 'RecallDue'
export type FollowUpTrigger =
  | 'MissedAppointment' | 'TreatmentPlanNotStarted' | 'TreatmentPlanInactive' | 'TreatmentUnfinished' | 'ProcedureCompleted' | 'RoutineRecall' | 'OverdueBalance'
export type FollowUpStatus = 'Pending' | 'Contacted' | 'Booked' | 'Recovered' | 'NoResponse' | 'Dismissed' | 'Resolved'
export type PriorityLevel = 'Low' | 'Medium' | 'High' | 'Urgent'
export type MessageChannel = 'WhatsApp' | 'Sms' | 'Email' | 'Phone'
export type MessageStatus = 'Queued' | 'Sent' | 'Delivered' | 'Read' | 'Failed' | 'ManualPending' | 'Logged' | 'Received'
export type MessagingConsent = 'Unknown' | 'Granted' | 'OptedOut'
export type AiPatientDataMode = 'Off' | 'Minimized' | 'Full'
export type MessageTemplateType = 'AppointmentReminder' | 'TreatmentFollowUp' | 'MissedAppointment' | 'Recall' | 'PaymentReminder' | 'PostTreatment' | 'Custom'
export type NotificationType =
  | 'NewAppointment' | 'UpcomingAppointment' | 'MissedAppointment' | 'OverduePayment' | 'TreatmentFollowUp' | 'HighValueInactiveTreatment'
  | 'NewPatient' | 'StaffAction' | 'System'
export type DocumentCategory = 'XRay' | 'Scan' | 'PatientDocument' | 'TreatmentImage' | 'Prescription' | 'Invoice' | 'Other'

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

// ------------------------------------------------------------------ auth
export type SubscriptionStatus = 'Trial' | 'Active' | 'PastDue' | 'Cancelled' | 'Suspended'
export interface ClinicInfo {
  id: string; name: string; nameAr?: string; currency: string; timeZone: string; defaultLanguage: string
  subscriptionPlan: string; subscriptionStatus: SubscriptionStatus; trialEndsAt?: string; paidUntil?: string
  /** Cancelled or suspended: records can be viewed and exported, not changed. */
  readOnly: boolean
}
export interface InvitationInfo { email: string; clinicName?: string; contactName?: string; plan: string; trialDays: number; expiresAt: string }
export interface UserProfile {
  id: string; email: string; fullName: string; fullNameAr?: string; role: string; permissions: string[]
  preferredLanguage: string; dentistId?: string; clinic: ClinicInfo
}
export interface LoginResponse { accessToken: string; accessTokenExpiresAt: string; user: UserProfile }

// ------------------------------------------------------------------ patients
export interface PatientListItem {
  id: string; fileNumber: string; fullName: string; fullNameAr?: string; phone: string; gender?: Gender; age?: number
  status: PatientStatus; lastVisitAt?: string; nextAppointmentAt?: string; outstandingBalance: number; openTreatmentValue: number
  primaryDentistName?: string; registeredAt: string; hasMedicalAlert: boolean
}
export interface PatientStats {
  completedVisits: number; noShows: number; upcomingAppointments: number; totalBilled: number; totalPaid: number
  outstandingBalance: number; openTreatmentValue: number; activePlans: number; nextAppointmentAt?: string; daysSinceLastVisit?: number
}
export interface PatientDetail {
  id: string; fileNumber: string; fullName: string; fullNameAr?: string; dateOfBirth?: string; age?: number; gender?: Gender
  phone: string; whatsAppNumber?: string; email?: string; address?: string; city?: string; nationalId?: string
  emergencyContactName?: string; emergencyContactPhone?: string; emergencyContactRelation?: string
  medicalNotes?: string; allergies?: string; medicalConditions?: string; currentMedications?: string
  insuranceProvider?: string; insurancePolicyNumber?: string; insuranceCoveragePercent?: number
  preferredLanguage: string; status: PatientStatus; registeredAt: string; referralSource?: string
  primaryDentistId?: string; primaryDentistName?: string; lastVisitAt?: string; stats: PatientStats
  messagingConsent: MessagingConsent; messagingConsentAt?: string; messagingConsentSource?: string
}
export type PatientUpsert = Omit<PatientDetail, 'id' | 'fileNumber' | 'age' | 'registeredAt' | 'primaryDentistName' | 'lastVisitAt' | 'stats' | 'messagingConsentAt' | 'messagingConsentSource'>
export interface TimelineEvent { kind: string; title: string; description?: string; date: string; amount?: number; status?: string; entityId?: string }
export interface PatientNote { id: string; content: string; isPinned: boolean; authorName?: string; createdAt: string }

// ------------------------------------------------------------------ staff
export interface Dentist {
  id: string; fullName: string; fullNameAr?: string; specialty?: string; phone?: string; email?: string; licenseNumber?: string
  color: string; isActive: boolean; userId?: string; upcomingAppointments: number; productionThisMonth: number
}
export interface StaffUser {
  id: string; email: string; fullName: string; fullNameAr?: string; phone?: string; roleId: string; roleName: string
  isActive: boolean; lastLoginAt?: string; createdAt: string; dentistId?: string
}
export interface Role { id: string; name: string; description?: string; isSystem: boolean; permissions: string[]; userCount: number }

// ------------------------------------------------------------------ appointments
export interface Appointment {
  id: string; patientId: string; patientName: string; patientNameAr?: string; patientPhone: string; patientFileNumber: string
  dentistId: string; dentistName: string; dentistColor: string; startTime: string; endTime: string; durationMinutes: number
  treatment: string; status: AppointmentStatus; notes?: string; chair?: string; treatmentPlanId?: string; fromRecovery: boolean
  reminderSentAt?: string; hasMedicalAlert: boolean
  /** Price agreed when booking. Not a debt until the payment is taken. */
  fee?: number
  /** Set once the fee was taken: the invoice it made, and what is still owed on it. */
  feeInvoiceId?: string; feeBalance?: number
}

// ------------------------------------------------------------------ clinical
export interface Procedure {
  id: string; code: string; name: string; nameAr?: string; category: ProcedureCategory; defaultPrice: number
  defaultDurationMinutes: number; resultingCondition?: ToothConditionType; isActive: boolean
}
export interface DentalConditionDto {
  id: string; toothNumber: number; type: ToothConditionType; status: ConditionStatus; surfaces?: string; notes?: string
  recordedAt: string; dentistName?: string
}
export interface ToothDto { toothNumber: number; isPrimary: boolean; notes?: string; conditions: DentalConditionDto[] }
export interface DentalChart { patientId: string; suggestPrimaryDentition: boolean; teeth: ToothDto[] }
export interface ToothHistoryEntry { kind: string; title: string; detail?: string; date: string; dentistName?: string; amount?: number; status?: string }

export interface TreatmentPlanListItem {
  id: string; planNumber: string; patientId: string; patientName: string; patientNameAr?: string; patientPhone: string
  dentistId: string; dentistName: string; title: string; status: TreatmentPlanStatus; totalAmount: number; paidAmount: number
  remainingAmount: number; completedValue: number; completionPercent: number; itemCount: number; completedItems: number
  createdAt: string; presentedAt?: string; lastActivityAt?: string; nextAppointmentAt?: string; daysSinceActivity: number
}
export interface TreatmentPlanItem {
  id: string; procedureId?: string; name: string; toothNumber?: number; surfaces?: string; quantity: number; unitPrice: number
  discount: number; total: number; status: TreatmentItemStatus; sequence: number; completedAt?: string; isInvoiced: boolean
}
export interface TreatmentPlanDetail {
  summary: TreatmentPlanListItem; notes?: string; discountAmount: number; subtotal: number; acceptedAt?: string; startedAt?: string
  completedAt?: string; rejectedAt?: string; rejectionReason?: string; items: TreatmentPlanItem[]
  invoices: { id: string; invoiceNumber: string; total: number; amountPaid: number; balance: number; status: InvoiceStatus; issueDate: string }[]
  appointments: { id: string; startTime: string; treatment: string; status: AppointmentStatus; dentistName: string }[]
  uninvoicedCompletedValue: number
}
export interface TreatmentRecord {
  id: string; procedureName: string; category?: ProcedureCategory; toothNumber?: number; surfaces?: string; performedAt: string
  price: number; notes?: string; dentistName: string; treatmentPlanItemId?: string
}
export interface ClinicalNote {
  id: string; noteDate: string; dentistId: string; dentistName: string; appointmentId?: string
  chiefComplaint?: string; diagnosis?: string; clinicalFindings?: string; treatmentPerformed?: string; recommendations?: string
  prescriptionNotes?: string; followUpInstructions?: string; freeText?: string; isAiAssisted: boolean; createdAt: string
}
export interface StructuredNote {
  chiefComplaint?: string; clinicalFindings?: string; assessment?: string; treatmentPerformed?: string; recommendation?: string
  prescriptionNotes?: string; followUp?: string; source: string; warnings: string[]
}

// ------------------------------------------------------------------ billing
export interface InvoiceListItem {
  id: string; invoiceNumber: string; patientId: string; patientName: string; patientNameAr?: string; patientPhone: string
  issueDate: string; dueDate: string; total: number; amountPaid: number; balance: number; status: InvoiceStatus
  isOverdue: boolean; daysOverdue: number; planNumber?: string; treatmentPlanId?: string
}
export interface InvoiceDetail {
  summary: InvoiceListItem; dentistName?: string; subtotal: number; discountAmount: number; taxAmount: number; notes?: string
  items: { id: string; description: string; procedureId?: string; treatmentPlanItemId?: string; toothNumber?: number; quantity: number; unitPrice: number; discount: number; total: number }[]
  payments: { id: string; receiptNumber: string; type: PaymentType; amount: number; method: PaymentMethod; paidAt: string; reference?: string; receivedBy?: string; isRecovered: boolean }[]
}
export interface PaymentListItem {
  id: string; receiptNumber: string; patientId: string; patientName: string; invoiceId?: string; invoiceNumber?: string
  type: PaymentType; amount: number; method: PaymentMethod; paidAt: string; reference?: string; receivedBy?: string; isRecovered: boolean
  notes?: string; refundedAmount: number
  /** An entry made by mistake and cancelled. It counts towards no total and is listed only on request. */
  isCancelled: boolean; cancelledAt?: string; cancelledBy?: string; cancelReason?: string
}
export interface PaymentSummary {
  today: number; thisMonth: number; refundsThisMonth: number; recoveredThisMonth: number
  byMethod: { method: PaymentMethod; amount: number; count: number }[]
}
export interface PatientBalance {
  patientId: string; patientName: string; patientNameAr?: string; phone: string; balance: number; openInvoices: number
  oldestDueDate: string; daysOverdue: number; lastPaymentAt?: string
}
export interface Expense {
  id: string; category: ExpenseCategory; description: string; amount: number; expenseDate: string; vendor?: string
  paymentMethod: PaymentMethod; reference?: string
}

// ------------------------------------------------------------------ recovery
export interface ScoreComponent { name: string; raw: number; weight: number; points: number }
export interface ScoreFacts {
  totalValue: number; daysSinceLastVisit: number; daysOverdue: number; issueCount: number; hasAcceptedPlan: boolean; noShows: number; completedVisits: number
}
export interface RecoveryScore {
  score: number; priority: PriorityLevel; returnLikelihood: number; likelihoodLabel: string
  components: ScoreComponent[]; reasons: string[]; facts: ScoreFacts
}
export interface RecoveryIssue {
  issueType: RecoveryIssueType; title: string; detail: string; value: number; triggeredAt: string; dueAt: string
  sourceEntityType: string; sourceEntityId: string; treatmentPlanId?: string; followUpId?: string; followUpStatus?: FollowUpStatus
}
export interface RecoveryPatient {
  patientId: string; fullName: string; fullNameAr?: string; phone: string; whatsAppNumber?: string; preferredLanguage: string
  lastVisitAt?: string; daysSinceVisit?: number; primaryIssue: RecoveryIssueType; issues: RecoveryIssue[]; totalValue: number
  score: RecoveryScore; isDue: boolean; dueAt: string; stage: 'New' | 'Contacted' | 'Recontact'; lastContactedAt?: string
  contactAttempts: number; recommendedAction: string; suggestedTemplate: MessageTemplateType; suggestedDentistId?: string
  suggestedTreatment?: string; suggestedPlanId?: string
}
export interface RecoverySummary {
  potentialRevenue: number; recoveredThisMonth: number; recoveredLastMonth: number; patientsToContact: number
  patientsInProgress: number; highValuePatients: number; missedAppointments: number; unfinishedTreatments: number
  notStartedTreatments: number; outstandingBalanceCount: number; outstandingBalanceTotal: number; overdueFollowUps: number
  recallsDue: number; recoveryRate: number; dueTodayValue: number
  byIssue: { issueType: RecoveryIssueType; count: number; value: number }[]
}
export interface RecoveryTrendPoint { period: string; recovered: number; patientsContacted: number; patientsRecovered: number }
export interface RecoveryWin { patientId: string; patientName: string; issueType: RecoveryIssueType; amount: number; resolvedAt: string; contactedBy?: string }
export interface RecoveryDashboard {
  summary: RecoverySummary; patients: RecoveryPatient[]; trend: RecoveryTrendPoint[]; recentWins: RecoveryWin[]; generatedAt: string; currency: string
}
export interface FollowUpRule {
  id: string; name: string; trigger: FollowUpTrigger; delayDays: number; procedureCategory?: ProcedureCategory
  messageTemplateId?: string; messageTemplateName?: string; priority: PriorityLevel; isEnabled: boolean; autoSend: boolean
}
export interface ContactResult { messageId?: string; messageStatus?: MessageStatus; manualSendUrl?: string; body?: string; followUpsUpdated: number }

// ------------------------------------------------------------------ messaging & notifications
export interface MessageTemplate {
  id: string; name: string; type: MessageTemplateType; channel: MessageChannel; language: string; body: string
  providerTemplateName?: string; isActive: boolean; isSystem: boolean
}
export interface Message {
  id: string; patientId: string; patientName: string; channel: MessageChannel; direction: 'Outbound' | 'Inbound'; body: string
  toNumber?: string; status: MessageStatus; error?: string; createdAt: string; sentAt?: string; sentBy?: string
  templateName?: string; isRecovery: boolean; isAutomated: boolean; deliveredAt?: string; readAt?: string
}
export interface Conversation {
  patientId: string; patientName: string; patientNameAr?: string; phone: string; lastMessage: string; lastMessageAt: string
  lastStatus: MessageStatus; messageCount: number
}
export interface MessagingStatus { provider: string; isConfigured: boolean; mode: string; note: string }
export interface SendResult { messageId: string; status: MessageStatus; body: string; manualSendUrl?: string; error?: string; provider: string }
export interface AppNotification {
  id: string; type: NotificationType; priority: PriorityLevel; title: string; body?: string; link?: string; createdAt: string; isRead: boolean
}

// ------------------------------------------------------------------ dashboard & reports
export interface Dashboard {
  todayAppointments: number; todayCompleted: number; todayRemaining: number; todayRevenue: number; outstandingPayments: number
  overdueInvoices: number; treatmentRevenuePotential: number; openTreatmentPlans: number; noShowRate30d: number
  newPatientsThisMonth: number
  recovery: { potentialRevenue: number; recoveredThisMonth: number; patientsRequiringFollowUp: number; highPriority: number; topPatients: RecoveryPatient[] }
  todaySchedule: Appointment[]
  revenueChart: { month: string; revenue: number; expenses: number; recovered: number }[]
  appointmentsChart: { weekStart: string; completed: number; noShow: number; cancelled: number; upcoming: number }[]
  treatmentFunnel: { step: string; count: number; value: number }[]
  retentionChart: { month: string; newPatients: number; returningPatients: number }[]
  recoveryChart: RecoveryTrendPoint[]
  currency: string
}
export interface NamedValue { name: string; value: number; count: number }
export interface Analytics {
  from: string; to: string; currency: string
  treatment: { plansCreated: number; plansPresented: number; plansAccepted: number; plansStarted: number; plansCompleted: number; plansAbandoned: number; acceptanceRate: number; completionRate: number; averageTreatmentValue: number; totalPlannedValue: number }
  patients: { patientsSeen: number; newPatients: number; returningPatients: number; returnRate: number; noShowRate: number; appointments: number; noShows: number; cancellations: number }
  recovery: { revenueRecovered: number; potentialLostRevenue: number; recoveryRate: number; patientsContacted: number; patientsRecovered: number; followUpsCreated: number }
  financial: { revenue: number; refunds: number; expenses: number; netProfit: number; invoiced: number; outstanding: number; production: number }
  revenueByMonth: Dashboard['revenueChart']
  productionByDentist: NamedValue[]; productionByCategory: NamedValue[]; topProcedures: NamedValue[]; paymentMethods: NamedValue[]
  expensesByCategory: NamedValue[]; recoveryByIssue: NamedValue[]; funnel: Dashboard['treatmentFunnel']
}

// ------------------------------------------------------------------ search, AI, settings, audit
export interface SearchHit { kind: 'patient' | 'appointment' | 'plan' | 'invoice'; id: string; title: string; subtitle?: string; meta?: string; link: string; patientId?: string }
export interface SearchResult { query: string; patients: SearchHit[]; appointments: SearchHit[]; treatmentPlans: SearchHit[]; invoices: SearchHit[]; elapsedMs: number }

export interface DataTable { title: string; columns: string[]; rows: string[][]; rowPatientIds?: (string | null)[] }
export interface AssistantAnswer { answer: string; source: string; toolsUsed: string[]; data: DataTable[]; refused: boolean }
export interface AssistantStatus {
  llmConfigured: boolean; mode: string; provider?: string; model?: string; suggestedQuestions: string[]; patientData: AiPatientDataMode
}

export interface RecoverySettings {
  missedAppointmentLookbackDays: number; maxLookbackDays: number; recontactAfterDays: number; recallEstimatedValue: number
  missedAppointmentDefaultValue: number; followUpVisitValue: number; highValueThreshold: number; attributionWindowDays: number
  weightValue: number; weightUrgency: number; weightLikelihood: number; weightSeverity: number
  highPriorityThreshold: number; mediumPriorityThreshold: number
}
export interface ClinicSettings {
  workingHoursStart: string; workingHoursEnd: string; workingDays: number[]; slotMinutes: number; appointmentReminderHours: number
  invoicePrefix: string; invoiceDueDays: number; taxRatePercent: number; whatsAppEnabled: boolean; autoSendFollowUps: boolean
  aiAssistantEnabled: boolean; recovery: RecoverySettings
  quietHoursStart: string; quietHoursEnd: string; requireConsentForAutomation: boolean
  /** Dates the clinic is closed, as yyyy-MM-dd. */
  holidays?: string[]
}
export interface ClinicProfile {
  id: string; name: string; nameAr?: string; phone?: string; email?: string; address?: string; city?: string; country: string
  timeZone: string; currency: string; defaultLanguage: string; subscriptionPlan: string
}
export interface Integrations { whatsAppProvider: string; whatsAppConfigured: boolean; aiConfigured: boolean; aiProvider: string; aiModel?: string; storageProvider: string }
export interface AuditLog {
  id: string; timestamp: string; userId?: string; userName?: string; action: string; entityType: string; entityId?: string
  changes?: string; ipAddress?: string
}
export interface DocumentDto {
  id: string; patientId: string; category: DocumentCategory; fileName: string; contentType: string; sizeBytes: number
  description?: string; toothNumber?: number; uploadedBy?: string; createdAt: string
}

// ------------------------------------------------------------------ onboarding
export interface OnboardingStep { key: string; kind: 'auto' | 'manual'; done: boolean; canConfirm: boolean; link: string; detail?: string }
export interface OnboardingFigures {
  patients: number; importedPatients: number; upcomingAppointments: number; outstandingBalance: number; importedOpeningBalance: number
  dentists: number; staffUsers: number; currency: string
}
export interface OnboardingStatus {
  steps: OnboardingStep[]; doneCount: number; totalCount: number; completed: boolean; completedAt?: string; figures: OnboardingFigures
}

// ------------------------------------------------------------------ import
export type ImportKind = 'Patients' | 'Appointments' | 'OpeningBalances' | 'TreatmentPlans'
export type ImportBatchStatus = 'Uploaded' | 'Committed' | 'Undone'
export type RowVerdict = 'Ready' | 'Duplicate' | 'PossibleDuplicate' | 'AlreadyImported' | 'Error'
export type RowAction = 'create' | 'skip' | 'fillBlanks'
export interface ImportField { key: string; label: string; labelAr: string; required: boolean; example: string; synonyms: string[]; help?: string }
export interface ImportDefinition { kind: ImportKind; fields: ImportField[]; requiredAnyOf: string[][] }
export interface ImportOptions {
  dateFormat: 'dmy' | 'mdy' | 'ymd'; allowMissingPhone: boolean; keepFileNumbers: boolean; duplicateAction: 'skip' | 'fillBlanks'
  possibleDuplicateAction: 'create' | 'skip'; assumePastAppointmentsCompleted: boolean; defaultDentistId?: string | null; defaultDurationMinutes: number
}
export type ImportMapping = Record<string, string | null>
export interface ImportRow {
  rowNumber: number; verdict: RowVerdict; action: RowAction; allowedActions: RowAction[]; errors: string[]; warnings: string[]
  display: string; matchId?: string; matchLabel?: string; amount?: number
}
export interface ImportSummary {
  totalRows: number; ready: number; duplicates: number; possibleDuplicates: number; alreadyImported: number; errors: number
  willCreate: number; willUpdate: number; willSkip: number; rowsWithWarnings: number; totalAmount?: number
}
export interface ImportPreview { summary: ImportSummary; rows: ImportRow[]; rowsTruncated: boolean; mappingProblems: string[] }
export interface ImportBatch {
  id: string; kind: ImportKind; fileName: string; sourceSystem: string; status: ImportBatchStatus; totalRows: number
  createdCount: number; updatedCount: number; skippedCount: number; errorCount: number; totalAmount?: number; createdAt: string
  createdBy?: string; committedAt?: string; undoneAt?: string; lastError?: string
}
export interface ImportUpload {
  batch: ImportBatch; headers: string[]; sampleRows: string[][]; suggestedMapping: ImportMapping; savedMapping?: ImportMapping
  savedOptions?: ImportOptions; previousImportAt?: string
}
export interface ImportCommit { batch: ImportBatch; summary: ImportSummary }
export interface ImportUndo { batch: ImportBatch; removed: number; kept: number; notes: string[] }

// ------------------------------------------------------------------ front desk
export interface FrontDeskAppointment { appointment: Appointment; patientBalance?: number | null; isLate: boolean; messagingConsent: MessagingConsent }
export interface FrontDesk {
  date: string; appointments: FrontDeskAppointment[]
  counts: { expected: number; arrived: number; inTreatment: number; completed: number; noShow: number; cancelled: number }
  tomorrowUnconfirmed: number; followUpsDue: number; collectedToday?: number | null; closed?: 'holiday' | 'day-off' | null
}

// ------------------------------------------------------------------ recovery evidence
export interface FollowUpContact {
  messageId: string; at: string; channel: MessageChannel; direction: 'Outbound' | 'Inbound'; status: MessageStatus; by?: string; isAutomated: boolean; body: string
}
export interface FollowUpTrail {
  id: string; issueType: RecoveryIssueType; title: string; sourceLabel?: string; sourceLink?: string; eventAt?: string; identifiedAt: string; dueDate: string
  status: FollowUpStatus; estimatedValue: number; contactAttempts: number; lastContactedAt?: string; outcome?: string; resolvedAt?: string; assignedTo?: string
  bookedAppointmentAt?: string; collectedAfterFollowUp: number; contacts: FollowUpContact[]
}

// ------------------------------------------------------------------ owner report
export interface ImpactPeriod {
  from: string; to: string; appointmentsDue: number; appointmentsCompleted: number; noShows: number; noShowRate: number; remindersSent: number
  followUpsIdentified: number; followUpsContacted: number; bookedAfterContact: number; paidAfterContact: number; collected: number
  collectedAfterFollowUp: number; plansPresented: number; plansAccepted: number; patientsSeen: number; returningPatients: number
}
export interface ImpactReport { current: ImpactPeriod; previous: ImpactPeriod; caveats: string[] }

// ------------------------------------------------------------------ account
export interface Account {
  clinicName: string; plan: string; status: SubscriptionStatus; trialEndsAt?: string; paidUntil?: string; statusReason?: string; readOnly: boolean
  createdAt: string; patients: number; activeStaff: number; dentists: number; paymentInstructions?: string; supportContact?: string
}
export interface WhatsAppConnection {
  mode: 'clinic' | 'shared' | 'manual'; canConnect: boolean; phoneNumberId?: string; displayPhoneNumber?: string; verifiedName?: string; connectedAt?: string
}
