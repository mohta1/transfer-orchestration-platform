using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Contracts.IntegrationEvents;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL manual operations")]
public sealed class ManualOperationsTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TASK-12 PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");

    private readonly MutableTimeProvider _clock =
        new(new DateTimeOffset(2026, 8, 12, 14, 0, 0, TimeSpan.Zero));

    private readonly Guid _operatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _correlationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    public async Task InitializeAsync()
    {
        await DropSchemasAsync();
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ManualRejectCreatesAuditRecordWithActorAndCorrelation()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);
        var operatorHeader = $"operator-{_operatorId:D}";

        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-reject-1",
            new { Reason = "Customer confirmed cancellation" },
            _correlationId,
            operatorHeader);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.AsNoTracking().SingleAsync();
        Assert.Equal(operatorHeader, audit.ActorId);
        Assert.Equal("RejectFromManualReview", audit.Action);
        Assert.Equal(transferId, audit.TransferId);
        Assert.Equal("ManualReviewRequired", audit.PreviousState);
        Assert.Equal("Rejected", audit.NewState);
        Assert.Equal("Customer confirmed cancellation", audit.Reason);
        Assert.Equal(_correlationId, audit.CorrelationId);

        var transfer = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .Transfers.AsNoTracking().SingleAsync(item => item.Id == new TransferId(transferId));
        Assert.Equal(TransferState.Rejected, transfer.State);
    }

    [Fact]
    public async Task MissingReasonIsRejectedWithoutAuditRecord()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);

        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-reject-no-reason",
            new { Reason = "   " },
            _correlationId,
            $"operator-{_operatorId:D}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.CountAsync());
    }

    [Fact]
    public async Task MissingOperatorIdentityIsRejectedWithoutAuditRecord()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/transfers/{transferId:D}/manual/reject")
        {
            Content = JsonContent.Create(new { Reason = "Valid reason" })
        };
        request.Headers.Add("Idempotency-Key", "manual-reject-no-operator");
        request.Headers.Add("X-Correlation-ID", _correlationId.ToString("D"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.CountAsync());
    }

    [Fact]
    public async Task RepeatedManualCommandReplaysWithoutDuplicateAuditOrFinancialEffect()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);
        var operatorHeader = $"operator-{_operatorId:D}";

        using var client = factory.CreateClient();
        using var first = ManualRequest(
            transferId,
            "manual-reject-replay",
            new { Reason = "Duplicate command test" },
            _correlationId,
            operatorHeader);
        using var second = ManualRequest(
            transferId,
            "manual-reject-replay",
            new { Reason = "Duplicate command test" },
            _correlationId,
            operatorHeader);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(second)).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.CountAsync());
        var account = await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
            .Accounts.Include(item => item.Reservations).AsNoTracking().SingleAsync();
        Assert.Equal(BalanceReservationStatus.Released, Assert.Single(account.Reservations).Status);
        Assert.Equal(500m, account.AvailableBalance);
    }

    [Fact]
    public async Task CorrelationFromHeaderPropagatesToAuditAndStructuredLogs()
    {
        var sink = new List<string>();
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock, sink);
        var transferId = await SeedManualReviewTransferAsync(factory);

        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-reject-logging",
            new { Reason = "Verify correlation logging" },
            _correlationId,
            $"operator-{_operatorId:D}");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_correlationId.ToString("D"), response.Headers.GetValues("X-Correlation-ID").Single());

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.AsNoTracking().SingleAsync();
        Assert.Equal(_correlationId, audit.CorrelationId);

        var combined = string.Join('\n', sink);
        Assert.Contains(_correlationId.ToString("D"), combined, StringComparison.Ordinal);
        Assert.Contains(transferId.ToString("D"), combined, StringComparison.Ordinal);
        Assert.Contains("CorrelationId", combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StructuredLogsDoNotContainSensitiveValues()
    {
        const string secretToken = "super-secret-bearer-token-12345";
        const string accountNumber = "1234567890123456";
        var sink = new List<string>();
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock, sink);
        var transferId = await SeedManualReviewTransferAsync(factory);

        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-reject-sensitive-log",
            new { Reason = $"Reviewed account {accountNumber}" },
            _correlationId,
            $"operator-{_operatorId:D}");
        request.Headers.Add("Authorization", $"Bearer {secretToken}");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var combined = string.Join('\n', sink);
        Assert.DoesNotContain(secretToken, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(accountNumber, combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManualConfirmSettlementPropagatesManualCorrelationToOutbox()
    {
        var originalCorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var manualCorrelationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory, originalCorrelationId);

        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-settle-correlation",
            new { Reason = "Verify outbox correlation propagation" },
            manualCorrelationId,
            $"operator-{_operatorId:D}",
            confirmSettlement: true);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var process = await transferContext.TransferProcessStates.AsNoTracking()
            .SingleAsync(item => item.TransferId == new TransferId(transferId));
        Assert.Equal(manualCorrelationId, process.CorrelationId);

        var message = await transferContext.OutboxMessages.AsNoTracking()
            .SingleAsync(item => item.TransferId == transferId);
        Assert.Equal(manualCorrelationId, message.CorrelationId);
        var payload = System.Text.Json.JsonSerializer.Deserialize<TransferCompletedIntegrationEvent>(message.Payload);
        Assert.Equal(manualCorrelationId, payload?.CorrelationId);
    }

    [Fact]
    public async Task ConcurrentDuplicateManualCommandReturnsReplayWithoutServerError()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);
        var operatorHeader = $"operator-{_operatorId:D}";

        using var client = factory.CreateClient();
        var first = ManualRequest(
            transferId,
            "manual-reject-concurrent",
            new { Reason = "Concurrent duplicate test" },
            _correlationId,
            operatorHeader);
        var second = ManualRequest(
            transferId,
            "manual-reject-concurrent",
            new { Reason = "Concurrent duplicate test" },
            _correlationId,
            operatorHeader);

        var responses = await Task.WhenAll(
            client.SendAsync(first),
            client.SendAsync(second));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.CountAsync());
    }

    [Fact]
    public async Task ManualConfirmSettlementConsumesReservationCompletesTransferAndAudits()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);

        using var client = factory.CreateClient();
        using var request = ManualRequest(
            transferId,
            "manual-settle-1",
            new { Reason = "External network confirmed settlement" },
            _correlationId,
            $"operator-{_operatorId:D}",
            confirmSettlement: true);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.AsNoTracking().SingleAsync();
        Assert.Equal("ConfirmSettlementFromManualReview", audit.Action);
        Assert.Equal("Completed", audit.NewState);

        var transfer = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .Transfers.AsNoTracking().SingleAsync(item => item.Id == new TransferId(transferId));
        Assert.Equal(TransferState.Completed, transfer.State);

        var account = await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
            .Accounts.Include(item => item.Reservations).AsNoTracking().SingleAsync();
        Assert.Equal(BalanceReservationStatus.Consumed, Assert.Single(account.Reservations).Status);
        Assert.Equal(400m, account.AvailableBalance);
    }

    [Fact]
    public async Task ManualOperationFromInvalidStateIsRejectedWithoutAuditRecord()
    {
        await using var factory = await OperationsFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewTransferAsync(factory);
        var operatorHeader = $"operator-{_operatorId:D}";

        using var client = factory.CreateClient();
        using var reject = ManualRequest(
            transferId,
            "manual-reject-first",
            new { Reason = "First rejection" },
            _correlationId,
            operatorHeader);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(reject)).StatusCode);

        using var invalid = ManualRequest(
            transferId,
            "manual-reject-second",
            new { Reason = "Second rejection should fail" },
            _correlationId,
            operatorHeader);
        var response = await client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.CountAsync());
    }

    [Fact]
    public async Task MigrationCreatesOperationsAuditRecordsTableOnPostgreSql()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'audit_operations'
                  AND table_name = 'operations_audit_records');
            """;
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    private Task<Guid> SeedManualReviewTransferAsync(WebApplicationFactory<Program> factory) =>
        SeedManualReviewTransferAsync(factory, _correlationId);

    private async Task<Guid> SeedManualReviewTransferAsync(WebApplicationFactory<Program> factory, Guid correlationId)
    {
        var gateway = new RecordingGateway
        {
            SubmissionResult = PaymentSubmissionResult.Timeout,
            StatusResult = PaymentStatusResult.Unknown
        };

        await using var scope = factory.Services.CreateAsyncScope();
        var accountId = Guid.NewGuid();
        var now = _clock.GetUtcNow();
        var transfer = Transfer.Create(accountId, Guid.NewGuid(), 100m, "GBP", TransferType.DomesticInterbank, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        transfer.RequestBalanceReservation(now);
        var process = TransferProcessState.Create(transfer.Id, correlationId, now);
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

    private static HttpRequestMessage ManualRequest(
        Guid transferId,
        string idempotencyKey,
        object body,
        Guid correlationId,
        string operatorHeader,
        bool confirmSettlement = false)
    {
        var path = confirmSettlement
            ? $"/api/transfers/{transferId:D}/manual/confirm-settlement"
            : $"/api/transfers/{transferId:D}/manual/reject";
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-ID", correlationId.ToString("D"));
        request.Headers.Add("X-Operator-ID", operatorHeader);
        return request;
    }

    private async Task DropSchemasAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DROP SCHEMA IF EXISTS audit_operations CASCADE;
            DROP SCHEMA IF EXISTS transfer_management CASCADE;
            DROP SCHEMA IF EXISTS account_balance CASCADE;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class OperationsFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly TimeProvider _clock;
        private readonly List<string>? _logSink;

        private OperationsFactory(string connectionString, TimeProvider clock, List<string>? logSink)
        {
            _connectionString = connectionString;
            _clock = clock;
            _logSink = logSink;
        }

        public static Task<OperationsFactory> CreateAsync(
            string connectionString,
            TimeProvider clock,
            List<string>? logSink = null) =>
            Task.FromResult(new OperationsFactory(connectionString, clock, logSink));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Database", _connectionString);
            builder.UseSetting("TransferManagement:Reconciliation:EscalationAttemptThreshold", "1");
            builder.UseSetting("TransferManagement:Reconciliation:RetryDelaySeconds", "5");
            builder.UseSetting("TransferManagement:Reconciliation:BatchSize", "20");
            builder.UseSetting("TransferManagement:Reconciliation:LeaseDurationSeconds", "30");
            builder.UseSetting("TransferManagement:Reconciliation:PollIntervalMilliseconds", "1000");

            builder.ConfigureServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IPaymentNetworkGateway>(new RecordingGateway
                {
                    SubmissionResult = PaymentSubmissionResult.Timeout,
                    StatusResult = PaymentStatusResult.Unknown
                }));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(_clock));

                if (_logSink is not null)
                {
                    services.AddSingleton<ILoggerProvider>(new TestLoggerProvider(_logSink));
                }
            });
        }
    }

    private sealed class RecordingGateway : IPaymentNetworkGateway
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

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public MutableTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }

    private sealed class TestLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, sink);

        public void Dispose()
        {
        }
    }

    private sealed class TestLogger(string categoryName, List<string> sink) : ILogger
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

[CollectionDefinition("PostgreSQL manual operations", DisableParallelization = true)]
public sealed class PostgreSqlManualOperationsGroup;
