namespace Tahona.Domain.Authorization;

/// <summary>
/// Fine-grained permissions. Roles are bags of permissions; endpoints require permissions, never role names,
/// so clinics can later customise roles without code changes.
/// </summary>
public static class Permissions
{
    public const string DashboardView = "dashboard.view";

    public const string PatientsView = "patients.view";
    public const string PatientsCreate = "patients.create";
    public const string PatientsEdit = "patients.edit";
    public const string PatientsDelete = "patients.delete";

    public const string AppointmentsView = "appointments.view";
    public const string AppointmentsManage = "appointments.manage";

    public const string ChartView = "chart.view";
    public const string ChartEdit = "chart.edit";

    public const string TreatmentView = "treatment.view";
    public const string TreatmentManage = "treatment.manage";

    public const string ClinicalView = "clinical.view";
    public const string ClinicalManage = "clinical.manage";

    public const string BillingView = "billing.view";
    public const string InvoicesManage = "invoices.manage";
    public const string PaymentsManage = "payments.manage";
    public const string PaymentsRefund = "payments.refund";

    public const string ExpensesView = "expenses.view";
    public const string ExpensesManage = "expenses.manage";

    public const string RecoveryView = "recovery.view";
    public const string RecoveryManage = "recovery.manage";

    public const string MessagesView = "messages.view";
    public const string MessagesSend = "messages.send";
    public const string TemplatesManage = "templates.manage";

    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";

    public const string ReportsView = "reports.view";
    public const string AiUse = "ai.use";

    public const string StaffManage = "staff.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";
    public const string DataImport = "data.import";
    public const string DataExport = "data.export";

    public static readonly IReadOnlyList<string> All =
    [
        DashboardView,
        PatientsView, PatientsCreate, PatientsEdit, PatientsDelete,
        AppointmentsView, AppointmentsManage,
        ChartView, ChartEdit,
        TreatmentView, TreatmentManage,
        ClinicalView, ClinicalManage,
        BillingView, InvoicesManage, PaymentsManage, PaymentsRefund,
        ExpensesView, ExpensesManage,
        RecoveryView, RecoveryManage,
        MessagesView, MessagesSend, TemplatesManage,
        DocumentsView, DocumentsManage,
        ReportsView, AiUse,
        StaffManage, SettingsManage, AuditView,
        DataImport, DataExport
    ];
}

public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string Dentist = "Dentist";
    public const string Receptionist = "Receptionist";
    public const string Accountant = "Accountant";

    public static readonly IReadOnlyDictionary<string, string[]> DefaultPermissions = new Dictionary<string, string[]>
    {
        [Admin] = [.. Permissions.All],
        [Dentist] =
        [
            Permissions.DashboardView,
            Permissions.PatientsView, Permissions.PatientsEdit,
            Permissions.AppointmentsView, Permissions.AppointmentsManage,
            Permissions.ChartView, Permissions.ChartEdit,
            Permissions.TreatmentView, Permissions.TreatmentManage,
            Permissions.ClinicalView, Permissions.ClinicalManage,
            Permissions.DocumentsView, Permissions.DocumentsManage,
            Permissions.RecoveryView, Permissions.MessagesView,
            Permissions.BillingView, Permissions.AiUse
        ],
        [Receptionist] =
        [
            Permissions.DashboardView,
            Permissions.PatientsView, Permissions.PatientsCreate, Permissions.PatientsEdit,
            Permissions.AppointmentsView, Permissions.AppointmentsManage,
            Permissions.TreatmentView, Permissions.ChartView,
            Permissions.BillingView, Permissions.PaymentsManage,
            Permissions.RecoveryView, Permissions.RecoveryManage,
            Permissions.MessagesView, Permissions.MessagesSend,
            Permissions.DocumentsView, Permissions.DocumentsManage,
            Permissions.AiUse
        ],
        [Accountant] =
        [
            Permissions.DashboardView,
            Permissions.PatientsView,
            Permissions.TreatmentView,
            Permissions.BillingView, Permissions.InvoicesManage, Permissions.PaymentsManage, Permissions.PaymentsRefund,
            Permissions.ExpensesView, Permissions.ExpensesManage,
            Permissions.RecoveryView, Permissions.ReportsView,
            Permissions.MessagesView, Permissions.MessagesSend,
            Permissions.AiUse
        ]
    };
}
