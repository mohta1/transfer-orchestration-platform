using TransferOrchestration.AccountBalance;
using TransferOrchestration.TransferManagement;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException(
        "Connection string 'Database' is not configured.");

builder.Services
    .AddTransferManagementModule(connectionString)
    .AddAccountBalanceModule(connectionString);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy"
}));

app.Run();
