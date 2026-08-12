using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Contracts.Queries;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL read and health API")]
public sealed class TransferReadAndHealthApiTests
{
    private const string InvalidConnectionString =
        "Host=127.0.0.1;Port=1;Database=invalid;Username=invalid;Password=invalid;Timeout=1;Command Timeout=1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Guid Source = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Destination = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetExistingTransferReturnsMappedDetailsAndCorrelationHeader()
    {
        var correlation = Guid.NewGuid();
        await using var factory = await ApiFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var submit = SubmitRequest("read-existing", Payload(), correlation);
        var submitResponse = await client.SendAsync(submit);
        Assert.Equal(HttpStatusCode.Accepted, submitResponse.StatusCode);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.NotNull(submitBody?.TransferId);

        using var getRequest = GetTransferRequest(submitBody.TransferId.Value, correlation);
        var getResponse = await client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(correlation.ToString("D"), getResponse.Headers.GetValues("X-Correlation-ID").Single());
        var transfer = await getResponse.Content.ReadFromJsonAsync<TransferDetailsDto>();
        Assert.NotNull(transfer);
        Assert.Equal(submitBody.TransferId, transfer.TransferId);
        Assert.Equal(Source, transfer.SourceAccountId);
        Assert.Equal(Destination, transfer.DestinationAccountId);
        Assert.Equal(10m, transfer.Amount);
        Assert.Equal("GBP", transfer.Currency);
        Assert.Equal("DomesticInterbank", transfer.TransferType);
        Assert.Equal("PendingBalanceReservation", transfer.State);
        Assert.Equal(correlation, transfer.CorrelationId);
        Assert.NotEqual(default, transfer.CreatedAtUtc);
        Assert.NotEqual(default, transfer.UpdatedAtUtc);
    }

    [Fact]
    public async Task GetUnknownTransferReturnsNotFoundProblemDetails()
    {
        await using var factory = await ApiFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = GetTransferRequest(Guid.NewGuid());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemDetails(response, "transfer_not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetTransferOwnedByAnotherCustomerReturnsNotFound()
    {
        var otherCustomerAccount = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await using var factory = await ApiFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var submit = SubmitRequest("read-other-customer", Payload());
        var submitResponse = await client.SendAsync(submit);
        Assert.Equal(HttpStatusCode.Accepted, submitResponse.StatusCode);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.NotNull(submitBody?.TransferId);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/transfers/{submitBody.TransferId:D}");
        getRequest.AuthorizeAsCustomer(otherCustomerAccount);
        var getResponse = await client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        await AssertProblemDetails(getResponse, "transfer_not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidPostReturnsBadRequestProblemDetails()
    {
        await using var factory = await ApiFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest(Guid.NewGuid().ToString(), Payload(amount: 0m));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemDetails(
            response,
            "validation_failed",
            HttpStatusCode.BadRequest,
            (body, problem) =>
            {
                Assert.Contains("errors", body, StringComparison.Ordinal);
                Assert.Contains("Amount must be greater than zero.", body, StringComparison.Ordinal);
                Assert.True(problem.Extensions.ContainsKey("errors"));
            });
    }

    [Fact]
    public async Task IdempotencyConflictReturnsConflictProblemDetails()
    {
        await using var factory = await ApiFactory.CreateAsync();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(SubmitRequest("conflict-key", Payload()))).StatusCode);
        var response = await client.SendAsync(SubmitRequest("conflict-key", Payload(amount: 11m)));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblemDetails(response, "idempotency_conflict", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AuthorizationRejectionReturnsForbiddenProblemDetails()
    {
        await using var factory = await ApiFactory.CreateAsync(authorization: DecisionOutcome.Rejected);
        using var client = factory.CreateClient();
        var response = await client.SendAsync(SubmitRequest("auth-rejected-problem", Payload()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemDetails(response, "authorization_rejected", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FraudRejectionReturnsUnprocessableEntityProblemDetails()
    {
        await using var factory = await ApiFactory.CreateAsync(fraud: DecisionOutcome.Rejected);
        using var client = factory.CreateClient();
        var response = await client.SendAsync(SubmitRequest("fraud-rejected-problem", Payload()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertProblemDetails(response, "fraud_rejected", HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task InternalExceptionDoesNotLeakImplementationDetails()
    {
        await using var factory = await ApiFactory.CreateAsync(throwAuthorization: true);
        using var client = factory.CreateClient();
        var response = await client.SendAsync(SubmitRequest("internal-error", Payload()));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await AssertProblemDetails(
            response,
            "internal_error",
            HttpStatusCode.InternalServerError,
            (body, _) =>
            {
                Assert.DoesNotContain("Injected authorization failure.", body);
                Assert.DoesNotContain("InvalidOperationException", body);
            });
    }

    [Fact]
    public async Task LivenessRemainsHealthyWhenDatabaseIsUnavailable()
    {
        await using var factory = await ApiFactory.CreateAsync(useInvalidDatabase: true);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
    }

    [Fact]
    public async Task ReadinessIsHealthyWhenDatabaseIsReachable()
    {
        await using var factory = await ApiFactory.CreateAsync();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Healthy", payload.Status);
        Assert.True(payload.Entries.ContainsKey("postgresql"));
        Assert.Equal("Healthy", payload.Entries["postgresql"].Status);
    }

    [Fact]
    public async Task ReadinessIsUnhealthyWhenDatabaseIsUnavailable()
    {
        await using var factory = await ApiFactory.CreateAsync(useInvalidDatabase: true);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<HealthResponse>(body, JsonOptions);
        Assert.NotNull(payload);
        Assert.Equal("Unhealthy", payload.Status);
        Assert.True(payload.Entries.ContainsKey("postgresql"));
        Assert.Equal("Unhealthy", payload.Entries["postgresql"].Status);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
    }

    private static object Payload(decimal amount = 10m) => new
    {
        SourceAccountId = Source,
        DestinationAccountId = Destination,
        Amount = amount,
        Currency = "GBP",
        TransferType = "DomesticInterbank"
    };

    private static HttpRequestMessage SubmitRequest(string key, object payload, Guid? correlation = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", key);
        request.AuthorizeAsCustomer(Source);
        if (correlation is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlation.Value.ToString("D"));
        }

        return request;
    }

    private static HttpRequestMessage GetTransferRequest(Guid transferId, Guid? correlation = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/transfers/{transferId:D}");
        request.AuthorizeAsCustomer(Source);
        if (correlation is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlation.Value.ToString("D"));
        }

        return request;
    }

    private static async Task AssertProblemDetails(
        HttpResponseMessage response,
        string expectedCode,
        HttpStatusCode expectedStatus,
        Action<string, ProblemDetails>? additionalAssertions = null)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Extensions["code"]?.ToString());
        Assert.Equal($"https://transfer-orchestration/errors/{expectedCode}", problem.Type);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        additionalAssertions?.Invoke(body, problem);
    }

    private sealed record SubmissionResponse(Guid? TransferId, Guid? CorrelationId, string? State, string Outcome);

    private sealed record HealthResponse(string Status, Dictionary<string, HealthEntry> Entries);

    private sealed record HealthEntry(string Status, string? Description);

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly bool _throwAuthorization;
        private readonly DecisionOutcome _authorization;
        private readonly DecisionOutcome _fraud;

        private ApiFactory(
            string connectionString,
            bool throwAuthorization,
            DecisionOutcome authorization,
            DecisionOutcome fraud)
        {
            _connectionString = connectionString;
            _throwAuthorization = throwAuthorization;
            _authorization = authorization;
            _fraud = fraud;
        }

        public static async Task<ApiFactory> CreateAsync(
            bool throwAuthorization = false,
            bool useInvalidDatabase = false,
            DecisionOutcome authorization = DecisionOutcome.Approved,
            DecisionOutcome fraud = DecisionOutcome.Approved)
        {
            if (useInvalidDatabase)
            {
                var invalidFactory = new ApiFactory(InvalidConnectionString, throwAuthorization, authorization, fraud);
                _ = invalidFactory.Services;
                return invalidFactory;
            }

            var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Destructive PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");
            await ResetSchemasAsync(connectionString);

            var factory = new ApiFactory(connectionString, throwAuthorization, authorization, fraud);
            _ = factory.Services;
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>().Database.MigrateAsync();
            return factory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Database", _connectionString);
            TestSecurityDefaults.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerAuthorization>();
                services.RemoveAll<IDailyTransferLimit>();
                services.RemoveAll<IFraudScreening>();
                services.AddSingleton<ICustomerAuthorization>(new ConfigurableAuthorization(_authorization, _throwAuthorization));
                services.AddSingleton<IDailyTransferLimit>(new AllowDailyLimit());
                services.AddSingleton<IFraudScreening>(new ConfigurableFraud(_fraud));
            });
        }

        private static async Task ResetSchemasAsync(string connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP SCHEMA IF EXISTS audit_operations CASCADE; DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
            await command.ExecuteNonQueryAsync();
        }

        private sealed class ConfigurableAuthorization(DecisionOutcome outcome, bool throwOnCall) : ICustomerAuthorization
        {
            public Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken)
            {
                if (throwOnCall)
                {
                    throw new InvalidOperationException("Injected authorization failure.");
                }

                return Task.FromResult(outcome);
            }
        }

        private sealed class AllowDailyLimit : IDailyTransferLimit
        {
            public Task<DecisionOutcome> TryConsumeAsync(Guid sourceAccountId, decimal amount, string currency, DateOnly utcDay, CancellationToken cancellationToken) =>
                Task.FromResult(DecisionOutcome.Approved);
        }

        private sealed class ConfigurableFraud(DecisionOutcome outcome) : IFraudScreening
        {
            public Task<DecisionOutcome> ScreenAsync(TransferSubmissionRequest request, CancellationToken cancellationToken) =>
                Task.FromResult(outcome);
        }
    }
}

[CollectionDefinition("PostgreSQL read and health API", DisableParallelization = true)]
public sealed class PostgreSqlReadAndHealthApiGroup;
