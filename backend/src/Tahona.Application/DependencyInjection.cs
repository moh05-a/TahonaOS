using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tahona.Application.Features.Ai;
using Tahona.Application.Features.Appointments;
using Tahona.Application.Features.Auth;
using Tahona.Application.Features.Billing;
using Tahona.Application.Features.Clinical;
using Tahona.Application.Features.Dashboard;
using Tahona.Application.Features.Documents;
using Tahona.Application.Features.Export;
using Tahona.Application.Features.Import;
using Tahona.Application.Features.Messaging;
using Tahona.Application.Features.Notifications;
using Tahona.Application.Features.Patients;
using Tahona.Application.Features.Provisioning;
using Tahona.Application.Features.Recovery;
using Tahona.Application.Features.Search;
using Tahona.Application.Features.Settings;
using Tahona.Application.Features.Staff;

namespace Tahona.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<AuthService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<PatientService>();
        services.AddScoped<DentistService>();
        services.AddScoped<StaffService>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<ProcedureService>();
        services.AddScoped<DentalChartService>();
        services.AddScoped<TreatmentPlanService>();
        services.AddScoped<ClinicalRecordsService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<ExpenseService>();
        services.AddScoped<RecoveryDetector>();
        services.AddScoped<RecoveryService>();
        services.AddScoped<RecoveryAttributionService>();
        services.AddScoped<RecoverySyncService>();
        services.AddScoped<FollowUpRuleService>();
        services.AddScoped<MessagingService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<SearchService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<AssistantTools>();
        services.AddScoped<AssistantService>();
        services.AddScoped<ClinicalNoteAiService>();
        services.AddScoped<ClinicProvisioningService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<AccountRecoveryService>();
        services.AddScoped<OnboardingService>();
        services.AddScoped<ClinicAdminService>();
        services.AddScoped<IImportHandler, PatientImportHandler>();
        services.AddScoped<IImportHandler, AppointmentImportHandler>();
        services.AddScoped<IImportHandler, BalanceImportHandler>();
        services.AddScoped<IImportHandler, TreatmentPlanImportHandler>();
        services.AddScoped<ImportService>();
        services.AddScoped<DataExportService>();
        services.AddScoped<FrontDeskService>();
        services.AddScoped<RecoveryHistoryService>();
        services.AddScoped<ImpactReportService>();
        services.AddScoped<AccountService>();
        services.AddScoped<WhatsAppSettingsService>();
        return services;
    }
}
