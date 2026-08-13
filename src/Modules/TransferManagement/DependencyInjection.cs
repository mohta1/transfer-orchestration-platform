using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.BalanceReservation;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;
using TransferOrchestration.TransferManagement.Infrastructure.Processing;
using TransferOrchestration.TransferManagement.Infrastructure.Submission;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Infrastructure.FraudScreening;
using TransferOrchestration.TransferManagement.Infrastructure.Outbox;
using TransferOrchestration.TransferManagement.Infrastructure.Reconciliation;
using TransferOrchestration.TransferManagement.Application.ManualOperations;
using TransferOrchestration.TransferManagement.Application.Queries;
using TransferOrchestration.TransferManagement.Contracts.ManualOperations;
using TransferOrchestration.TransferManagement.Contracts.Queries;

namespace TransferOrchestration.TransferManagement;

public static class DependencyInjection
{
    public static IServiceCollection AddTransferManagementModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<TransferManagementDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                    npgsqlOptions.MigrationsHistoryTable(
                        "__EFMigrationsHistory",
                        TransferManagementDbContext.Schema)));
        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<ITransferProcessStateRepository, TransferProcessStateRepository>();
        services.AddScoped<ITransferProcessManager, TransferProcessManager>();
        services.AddScoped<ITransferSubmissionIdempotencyStore, TransferSubmissionIdempotencyStore>();
        services.AddScoped<ITransferManagementTransaction, TransferManagementTransaction>();
        services.AddScoped<ITransferSubmissionService, TransferSubmissionService>();
        services.AddScoped<IFraudScreeningProcessStep, FraudScreeningProcessStep>();
        services.AddScoped<IFraudScreeningDueWorkDispatcher, FraudScreeningDueWorkDispatcher>();
        services.AddScoped<IReserveBalanceProcessStep, ReserveBalanceProcessStep>();
        services.AddScoped<ITransferProcessDueWorkDispatcher, TransferProcessDueWorkDispatcher>();
        services.AddScoped<IPaymentSubmissionProcessStep, PaymentSubmissionProcessStep>();
        services.AddScoped<IPaymentSubmissionDueWorkDispatcher, PaymentSubmissionDueWorkDispatcher>();
        services.AddScoped<IReconciliationRecordRepository, ReconciliationRecordRepository>();
        services.AddScoped<IReconciliationStore, ReconciliationStore>();
        services.AddScoped<IReconciliationScheduling, ReconciliationScheduling>();
        services.AddScoped<IReconciliationProcessStep, ReconciliationProcessStep>();
        services.AddScoped<IReconciliationDueWorkDispatcher, ReconciliationDueWorkDispatcher>();
        services.AddScoped<ITransferManualOperations, TransferManualOperationsService>();
        services.AddScoped<ITransferQueries, TransferQueries>();
        services.AddHostedService<TransferProcessWorker>();
        services.AddHostedService<ReconciliationWorker>();
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddScoped<OutboxBatchDispatcher>();
        services.AddHostedService<OutboxWorker>();
        services.AddScoped<ICustomerAuthorization, AuthenticatedCustomerAuthorization>();
        services.AddScoped<IDailyTransferLimit, ConfiguredDailyTransferLimit>();
        services.AddScoped<IFraudScreening, AllowFraudScreening>();
        services.AddSingleton(TimeProvider.System);
        services.Configure<SubmissionPolicyOptions>(configuration.GetSection(SubmissionPolicyOptions.SectionName));
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.InitialRetryDelaySeconds <= options.MaxRetryDelaySeconds,
                "InitialRetryDelay must not exceed MaxRetryDelay.")
            .ValidateOnStart();
        services.AddOptions<FraudScreeningOptions>()
            .Bind(configuration.GetSection(FraudScreeningOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => options.InitialRetryDelaySeconds <= options.MaxRetryDelaySeconds,
                "InitialRetryDelay must not exceed MaxRetryDelay.")
            .ValidateOnStart();
        services.AddOptions<ReconciliationOptions>()
            .Bind(configuration.GetSection(ReconciliationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
