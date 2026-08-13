using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL security boundary")]
public sealed class SecurityBoundaryTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Guid Source = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Destination = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherCustomerAccount = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly MutableTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 12, 14, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task PostTransferUnauthenticatedRejected()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("unauthenticated", Payload());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemDetails(response, "unauthorized", HttpStatusCode.Unauthorized);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task PostTransferAuthorizedSucceeds()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("authorized", Payload());
        request.AuthorizeAsCustomer(Source);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task PostTransferWrongAccountForbidden()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("wrong-account", Payload());
        request.AuthorizeAsCustomer(OtherCustomerAccount);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemDetails(response, "authorization_rejected", HttpStatusCode.Forbidden);
        await factory.AssertSingleTransferStateAsync(TransferState.Rejected);
    }

    [Fact]
    public async Task PostTransferMissingAccountClaimForbidden()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("missing-account-claim", Payload());
        request.AuthorizeAsCustomerWithoutAccountClaim();
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemDetails(response, "authorization_rejected", HttpStatusCode.Forbidden);
        await factory.AssertSingleTransferStateAsync(TransferState.Rejected);
    }

    [Fact]
    public async Task GetTransferCrossCustomerReturnsNotFound()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var submit = SubmitRequest("read-cross-customer", Payload());
        submit.AuthorizeAsCustomer(Source);
        var submitResponse = await client.SendAsync(submit);
        Assert.Equal(HttpStatusCode.Accepted, submitResponse.StatusCode);
        var submitBody = await submitResponse.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.NotNull(submitBody?.TransferId);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/transfers/{submitBody.TransferId:D}");
        getRequest.AuthorizeAsCustomer(OtherCustomerAccount);
        var getResponse = await client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        await AssertProblemDetails(getResponse, "transfer_not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ManualCommandOrdinaryUserForbidden()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        var transferId = await factory.SeedManualReviewTransferAsync();
        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-customer-forbidden",
            new { Reason = "Should not succeed" });
        request.AuthorizeAsCustomer(Source);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemDetails(response, "forbidden", HttpStatusCode.Forbidden);
        Assert.Equal(0, await factory.AuditCountAsync());
    }

    [Fact]
    public async Task ManualOperatorSucceeds()
    {
        const string operatorSubject = "operator-security-test";
        await using var factory = await SecurityFactory.CreateAsync();
        var transferId = await factory.SeedManualReviewTransferAsync();
        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-operator-success",
            new { Reason = "Operator approved rejection" });
        request.AuthorizeAsOperator(operatorSubject);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await factory.AuditCountAsync());
    }

    [Fact]
    public async Task ActorReachesAudit()
    {
        const string operatorSubject = "operator-audit-actor";
        await using var factory = await SecurityFactory.CreateAsync();
        var transferId = await factory.SeedManualReviewTransferAsync();
        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-audit-actor",
            new { Reason = "Verify trusted actor identity" });
        request.AuthorizeAsOperator(operatorSubject);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.AsNoTracking().SingleAsync();
        Assert.Equal(operatorSubject, audit.ActorId);
    }

    [Fact]
    public async Task TokenSecretRedactionChecked()
    {
        const string secretToken = "super-secret-bearer-token-12345";
        var sink = new List<string>();
        await using var factory = await SecurityFactory.CreateAsync(logSink: sink);
        var transferId = await factory.SeedManualReviewTransferAsync();
        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-token-redaction",
            new { Reason = "Verify token is not logged" });
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secretToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var combined = string.Join('\n', sink);
        Assert.DoesNotContain(secretToken, combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedTokenRejectedWithoutBusinessEffect()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("malformed-token", Payload());
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateMalformedToken());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task InvalidSignatureRejectedWithoutBusinessEffect()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("invalid-signature", Payload());
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateCustomerTokenWithWrongSignature(Source));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task ExpiredTokenRejectedWithoutBusinessEffect()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("expired-token", Payload());
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateExpiredCustomerToken(Source));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task IncorrectIssuerRejectedWithoutBusinessEffect()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("wrong-issuer", Payload());
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateCustomerTokenWithIssuer(Source, "wrong-issuer"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task IncorrectAudienceRejectedWithoutBusinessEffect()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = SubmitRequest("wrong-audience", Payload());
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            TestJwtTokenFactory.CreateCustomerTokenWithAudience(Source, "wrong-audience"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Fact]
    public async Task HealthEndpointsRemainAnonymous()
    {
        await using var factory = await SecurityFactory.CreateAsync();
        using var client = factory.CreateClient();
        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    [Fact]
    public async Task ClientSuppliedOperatorHeaderCannotImpersonateAuditActor()
    {
        const string trustedOperator = "trusted-operator-subject";
        const string forgedOperator = "forged-operator-header";
        await using var factory = await SecurityFactory.CreateAsync();
        var transferId = await factory.SeedManualReviewTransferAsync();
        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-no-header-impersonation",
            new { Reason = "Header must not override authenticated actor" });
        request.AuthorizeAsOperator(trustedOperator);
        request.Headers.Add("X-Operator-ID", forgedOperator);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.AsNoTracking().SingleAsync();
        Assert.Equal(trustedOperator, audit.ActorId);
        Assert.NotEqual(forgedOperator, audit.ActorId);
    }

    private static object Payload(decimal amount = 10m) => new
    {
        SourceAccountId = Source,
        DestinationAccountId = Destination,
        Amount = amount,
        Currency = "GBP",
        TransferType = "DomesticInterbank"
    };

    private static HttpRequestMessage SubmitRequest(string key, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static HttpRequestMessage ManualRequest(Guid transferId, string idempotencyKey, object body) =>
        new(HttpMethod.Post, $"/api/transfers/{transferId:D}/manual/reject")
        {
            Content = JsonContent.Create(body),
            Headers =
            {
                { "Idempotency-Key", idempotencyKey }
            }
        };

    private sealed record SubmissionResponse(Guid? TransferId);

    private static async Task AssertProblemDetails(
        HttpResponseMessage response,
        string expectedCode,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(expectedCode, problem.Extensions["code"]?.ToString());
        Assert.DoesNotContain("SigningKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", body, StringComparison.Ordinal);
    }

    private sealed class SecurityFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly List<string>? _logSink;

        private SecurityFactory(string connectionString, List<string>? logSink)
        {
            _connectionString = connectionString;
            _logSink = logSink;
        }

        public static async Task<SecurityFactory> CreateAsync(List<string>? logSink = null)
        {
            var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
                ?? throw new InvalidOperationException("PostgreSQL security tests require TEST_DATABASE_CONNECTION_STRING.");
            await ResetSchemasAsync(connectionString);
            var factory = new SecurityFactory(connectionString, logSink);
            _ = factory.Services;
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
            await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>().Database.MigrateAsync();
            return factory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Database", _connectionString);
            TestSecurityDefaults.ConfigureWebHost(builder);
            builder.UseSetting("TransferManagement:Reconciliation:EscalationAttemptThreshold", "1");
            builder.UseSetting("TransferManagement:Reconciliation:RetryDelaySeconds", "5");
            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IPaymentNetworkGateway>(new RecordingGateway
                {
                    SubmissionResult = PaymentSubmissionResult.Timeout,
                    StatusResult = PaymentStatusResult.Unknown
                }));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.RemoveAll<IDailyTransferLimit>();
                services.RemoveAll<IFraudScreening>();
                services.AddSingleton<IDailyTransferLimit>(new AllowAllDailyLimit());
                services.AddSingleton<IFraudScreening>(new AllowAllFraud());
                if (_logSink is not null)
                {
                    services.AddSingleton<ILoggerProvider>(new SecurityTestLoggerProvider(_logSink));
                }

                services.RemoveHostedWorkers();
            });
        }

        public async Task<Guid> SeedManualReviewTransferAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var accountId = Source;
            var now = Clock.GetUtcNow();
            var transfer = Transfer.Create(accountId, Destination, 100m, "GBP", TransferType.DomesticInterbank, now);
            transfer.Submit(now);
            transfer.RequestAuthorisation(now);
            transfer.Authorise(now);
            transfer.BeginFraudScreening(now);
            transfer.RequestBalanceReservation(now);
            var process = TransferProcessState.Create(transfer.Id, Guid.NewGuid(), now);
            process.Schedule(TransferProcessAction.ReserveBalance, now, now);

            var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
            accountContext.Accounts.Add(Account.Create(accountId, "GBP", 500m, AccountStatus.Active));
            await accountContext.SaveChangesAsync();

            var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            transferContext.AddRange(transfer, process);
            await transferContext.SaveChangesAsync();

            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<IReconciliationDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));

            var snapshot = await transferContext.Transfers.AsNoTracking().SingleAsync(item => item.Id == transfer.Id);
            Assert.Equal(TransferState.ManualReviewRequired, snapshot.State);
            return transfer.Id.Value;
        }

        public async Task<int> TransferCountAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Transfers.CountAsync();
        }

        public async Task AssertSingleTransferStateAsync(TransferState expectedState)
        {
            await using var scope = Services.CreateAsyncScope();
            var transfer = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
                .Transfers.AsNoTracking().SingleAsync();
            Assert.Equal(expectedState, transfer.State);
        }

        public async Task<int> AuditCountAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>().OperationsAuditRecords.CountAsync();
        }

        private static async Task ResetSchemasAsync(string connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "DROP SCHEMA IF EXISTS audit_operations CASCADE; DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class AllowAllDailyLimit : IDailyTransferLimit
    {
        public Task<DecisionOutcome> TryConsumeAsync(
            Guid sourceAccountId,
            decimal amount,
            string currency,
            DateOnly utcDay,
            CancellationToken cancellationToken) =>
            Task.FromResult(DecisionOutcome.Approved);
    }

    private sealed class AllowAllFraud : IFraudScreening
    {
        public Task<FraudScreeningResult> ScreenAsync(FraudScreeningRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(FraudScreeningResult.Approved);
    }

    private sealed class SecurityTestLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SecurityTestLogger(categoryName, sink);

        public void Dispose()
        {
        }
    }

    private sealed class SecurityTestLogger(string categoryName, List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            sink.Add(state?.ToString() ?? string.Empty);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Add($"{categoryName}: {formatter(state, exception)}");
    }
}

[CollectionDefinition("PostgreSQL security boundary", DisableParallelization = true)]
public sealed class PostgreSqlSecurityBoundaryGroup;

internal sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan value) => _now += value;
}

internal sealed class RecordingGateway : IPaymentNetworkGateway
{
    public PaymentSubmissionResult SubmissionResult { get; init; } = PaymentSubmissionResult.Accepted;

    public PaymentStatusResult StatusResult { get; set; } = PaymentStatusResult.Unknown;

    public NetworkSubmissionReference CreateSubmissionReference(Guid transferId) =>
        new($"TEST-{transferId:N}".ToUpperInvariant());

    public Task<PaymentSubmissionResult> SubmitAsync(
        PaymentSubmissionRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(SubmissionResult);

    public Task<PaymentStatusResult> GetStatusAsync(
        NetworkSubmissionReference reference,
        CancellationToken cancellationToken) =>
        Task.FromResult(StatusResult);
}
