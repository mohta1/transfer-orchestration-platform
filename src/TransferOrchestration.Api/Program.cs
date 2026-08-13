using Microsoft.Extensions.Diagnostics.HealthChecks;
using TransferOrchestration.AccountBalance;
using TransferOrchestration.AuditOperations;
using TransferOrchestration.AuditOperations.Api;
using TransferOrchestration.AuditOperations.Infrastructure;
using TransferOrchestration.Api.Infrastructure;
using TransferOrchestration.Api.Infrastructure.Security;
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

builder.Services.AddApiSecurity(builder.Configuration);

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Application process is running."), tags: ["live"])
    .AddCheck<PostgreSqlHealthCheck>("postgresql", tags: ["ready"]);

var app = builder.Build();

app.UseSafeExceptionHandling();
app.UseCorrelationContext();
app.UseAuthentication();
app.UseAuthorization();
app.UseOperatorIdentity();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.MapTransferSubmissionEndpoints();
app.MapTransferReadEndpoints();
app.MapManualOperationsEndpoints();
app.MapOperationsEndpoints();

app.Run();

public partial class Program;
