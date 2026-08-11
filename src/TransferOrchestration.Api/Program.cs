using TransferOrchestration.AccountBalance;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Api;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "Connection string 'Database' is not configured.");

builder.Services
    .AddTransferManagementModule(connectionString, builder.Configuration)
    .AddAccountBalanceModule(connectionString);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy"
}));
app.MapTransferSubmissionEndpoints();

app.Run();

public partial class Program;
