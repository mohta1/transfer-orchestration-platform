using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.BalanceReservation;
using TransferOrchestration.TransferManagement.Application.Persistence;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Idempotency;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence.Repositories;
using TransferOrchestration.TransferManagement.Infrastructure.Processing;
using TransferOrchestration.TransferManagement.Infrastructure.Submission;

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
        services.AddScoped<IReserveBalanceProcessStep, ReserveBalanceProcessStep>();
        services.AddScoped<ITransferProcessDueWorkDispatcher, TransferProcessDueWorkDispatcher>();
        services.AddHostedService<TransferProcessWorker>();
        services.AddScoped<ICustomerAuthorization, AllowCustomerAuthorization>();
        services.AddScoped<IDailyTransferLimit, ConfiguredDailyTransferLimit>();
        services.AddScoped<IFraudScreening, AllowFraudScreening>();
        services.AddSingleton(TimeProvider.System);
        services.Configure<SubmissionPolicyOptions>(configuration.GetSection(SubmissionPolicyOptions.SectionName));

        return services;
    }
}
