using TransferOrchestration.AccountBalance;
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
    .AddNotificationModule(connectionString)
    .AddAccountBalanceModule(connectionString)
    .AddPaymentNetworkModule();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy"
}));
app.MapTransferSubmissionEndpoints();

app.Run();

public partial class Program;
