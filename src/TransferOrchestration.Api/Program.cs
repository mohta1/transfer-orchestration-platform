using TransferOrchestration.AccountBalance;
using TransferOrchestration.AuditOperations;
using TransferOrchestration.AuditOperations.Api;
using TransferOrchestration.AuditOperations.Infrastructure;
using TransferOrchestration.PaymentNetwork;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Api;
using TransferOrchestration.Notification;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "Connection string 'Database' is not configured.");

builder.Services
    .AddTransferManagementModule(connectionString, builder.Configuration)
    .AddNotificationModule(connectionString, builder.Configuration)
    .AddAccountBalanceModule(connectionString)
    .AddPaymentNetworkModule()
    .AddAuditOperationsModule(connectionString);

var app = builder.Build();

app.UseCorrelationContext();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy"
}));
app.MapTransferSubmissionEndpoints();
app.MapManualOperationsEndpoints();

app.Run();

public partial class Program;
