using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Reconciliation;
using TransferOrchestration.TransferManagement.Contracts.Queries;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL stuck transfers")]
public sealed class StuckTransferOperationsTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TASK-20 PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");

    private readonly MutableTimeProvider _clock =
        new(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero));

    private const int ThresholdSeconds = 60;

    public async Task InitializeAsync()
    {
        await DropSchemasAsync();
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OldEligibleProcessAppearsInOperatorQuery()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedStuckTransferAsync(
            factory,
            TransferState.PendingFraudScreening,
            updatedAt: _clock.GetUtcNow().AddMinutes(-5));

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-old");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.NotNull(body);
        Assert.Contains(body!.Items, item => item.TransferId == transferId);
    }

    [Fact]
    public async Task RecentEligibleProcessDoesNotAppear()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            TransferState.PendingFraudScreening,
            updatedAt: _clock.GetUtcNow().AddSeconds(-10));

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-recent");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.NotNull(body);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task FutureScheduledWorkDoesNotAppear()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            TransferState.PendingFraudScreening,
            updatedAt: _clock.GetUtcNow().AddMinutes(-30),
            nextAttemptAt: _clock.GetUtcNow().AddMinutes(10));

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-future");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task OverdueDueWorkAppears()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedStuckTransferAsync(
            factory,
            TransferState.PendingFraudScreening,
            updatedAt: _clock.GetUtcNow().AddMinutes(-30),
            nextAttemptAt: _clock.GetUtcNow().AddMinutes(-5));

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-overdue");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.Contains(body!.Items, item =>
            item.TransferId == transferId && item.Category == "OverdueScheduledWork");
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Rejected")]
    [InlineData("FraudRejected")]
    public async Task TerminalStatesDoNotAppear(string stateName)
    {
        var terminalState = Enum.Parse<TransferState>(stateName);
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            terminalState,
            updatedAt: _clock.GetUtcNow().AddHours(-2),
            processCompleted: true);

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-terminal");
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task ManualReviewRequiredAppearsWithCategory()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedStuckTransferAsync(
            factory,
            TransferState.ManualReviewRequired,
            updatedAt: _clock.GetUtcNow().AddMinutes(-10),
            processCompleted: true);

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-manual");
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        var item = Assert.Single(body!.Items, candidate => candidate.TransferId == transferId);
        Assert.Equal("ManualReviewRequired", item.Category);
    }

    [Fact]
    public async Task SubmissionStatusUnknownWithFutureReconciliationDoesNotAppear()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            TransferState.SubmissionStatusUnknown,
            updatedAt: _clock.GetUtcNow().AddMinutes(-30),
            reconciliationNextAttempt: _clock.GetUtcNow().AddMinutes(15));

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-recon-future");
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.Empty(body!.Items);
    }

    [Fact]
    public async Task MultipleResultsAreOrderedAndBounded()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            TransferState.PendingFraudScreening,
            updatedAt: _clock.GetUtcNow().AddMinutes(-20));
        await SeedStuckTransferAsync(
            factory,
            TransferState.ManualReviewRequired,
            updatedAt: _clock.GetUtcNow().AddMinutes(-40),
            processCompleted: true);
        await SeedStuckTransferAsync(
            factory,
            TransferState.BalanceReserved,
            updatedAt: _clock.GetUtcNow().AddMinutes(-10));

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest(maxResults: 2);
        request.AuthorizeAsOperator("operator-stuck-bounded");
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.Equal(2, body!.Items.Count);
        Assert.True(body.Items[0].AgeSeconds >= body.Items[1].AgeSeconds);
    }

    [Fact]
    public async Task ProjectionContainsNoSensitiveFields()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            TransferState.ManualReviewRequired,
            updatedAt: _clock.GetUtcNow().AddMinutes(-15),
            processCompleted: true);

        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-projection");
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("sourceAccount", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destinationAccount", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idempotency", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NetworkSubmissionReference", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RestartYieldsSameDetectionFromDurableState()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedStuckTransferAsync(
            factory,
            TransferState.ManualReviewRequired,
            updatedAt: _clock.GetUtcNow().AddMinutes(-20),
            processCompleted: true);

        await using var secondFactory = await StuckFactory.CreateAsync(_connectionString, _clock);
        using var client = secondFactory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsOperator("operator-stuck-restart");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);

        Assert.Contains(body!.Items, item => item.TransferId == transferId);
    }

    [Fact]
    public async Task ConcurrentDetectionDoesNotMutateAuditOrBusinessState()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        await SeedStuckTransferAsync(
            factory,
            TransferState.ManualReviewRequired,
            updatedAt: _clock.GetUtcNow().AddMinutes(-20),
            processCompleted: true);

        using var client = factory.CreateClient();
        var tasks = Enumerable.Range(0, 5).Select(_ =>
        {
            var request = StuckTransfersRequest();
            request.AuthorizeAsOperator("operator-stuck-concurrent");
            return client.SendAsync(request);
        });
        await Task.WhenAll(tasks);

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.CountAsync());
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .Transfers.CountAsync());
    }

    [Fact]
    public async Task UnauthenticatedRequestReturns401ProblemDetails()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/operations/stuck-transfers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemDetailsAsync(response, "unauthorized", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CustomerRequestReturns403()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest();
        request.AuthorizeAsCustomer(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertProblemDetailsAsync(response, "forbidden", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CorrelationHeaderIsReturned()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        var correlationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        using var client = factory.CreateClient();
        using var request = StuckTransfersRequest(correlationId);
        request.AuthorizeAsOperator("operator-stuck-correlation");
        var response = await client.SendAsync(request);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Equal(correlationId.ToString("D"), Assert.Single(values));
    }

    [Fact]
    public async Task DiscoveredTransferSupportsManualRecoveryWithAudit()
    {
        await using var factory = await StuckFactory.CreateAsync(_connectionString, _clock);
        var transferId = await SeedManualReviewWithReservationAsync(factory, _clock);
        _clock.Advance(TimeSpan.FromMinutes(5));

        using var client = factory.CreateClient();
        using var listRequest = StuckTransfersRequest();
        listRequest.AuthorizeAsOperator("operator-stuck-recovery");
        var listResponse = await client.SendAsync(listRequest);
        var listBody = await listResponse.Content.ReadFromJsonAsync<StuckTransferQueryResult>(JsonOptions);
        Assert.Contains(listBody!.Items, item => item.TransferId == transferId);

        using var rejectRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/transfers/{transferId:D}/manual/reject")
        {
            Content = JsonContent.Create(new { Reason = "Recovered from stuck list" })
        };
        rejectRequest.Headers.Add("Idempotency-Key", "stuck-recovery-reject");
        rejectRequest.AuthorizeAsOperator("operator-stuck-recovery");
        var rejectResponse = await client.SendAsync(rejectRequest);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<AuditOperationsDbContext>()
            .OperationsAuditRecords.AsNoTracking().SingleAsync();
        Assert.Equal("operator-stuck-recovery", audit.ActorId);
    }

    private static async Task<Guid> SeedManualReviewWithReservationAsync(
        WebApplicationFactory<Program> factory,
        MutableTimeProvider clock)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accountId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = clock.GetUtcNow();
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

    private static async Task<Guid> SeedStuckTransferAsync(
        WebApplicationFactory<Program> factory,
        TransferState state,
        DateTimeOffset updatedAt,
        DateTimeOffset? nextAttemptAt = null,
        bool processCompleted = false,
        DateTimeOffset? reconciliationNextAttempt = null)
    {
        var correlationId = Guid.NewGuid();
        var createdAt = updatedAt.AddMinutes(-30);
        await using var scope = factory.Services.CreateAsyncScope();
        var transfer = Transfer.Create(Guid.NewGuid(), Guid.NewGuid(), 50m, "GBP", TransferType.DomesticInterbank, createdAt);
        transfer.Submit(createdAt);
        ApplyTransferState(transfer, state, updatedAt);

        var process = TransferProcessState.Create(transfer.Id, correlationId, createdAt);
        if (processCompleted)
        {
            process.Complete(updatedAt);
        }
        else if (nextAttemptAt is null)
        {
            process.MarkWaiting(updatedAt);
        }
        else
        {
            process.Schedule(TransferProcessAction.RequestFraudScreening, nextAttemptAt.Value, updatedAt);
        }

        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        context.AddRange(transfer, process);

        if (state == TransferState.SubmissionStatusUnknown && reconciliationNextAttempt is not null)
        {
            var record = ReconciliationRecord.ScheduleForUnknown(
                transfer.Id,
                $"ref-{transfer.Id.Value:N}",
                createdAt);
            record.RecordUnknownAttempt(reconciliationNextAttempt.Value, nameof(PaymentStatusResult.Unknown), updatedAt);
            context.ReconciliationRecords.Add(record);
        }

        await context.SaveChangesAsync();
        return transfer.Id.Value;
    }

    private static void ApplyTransferState(Transfer transfer, TransferState state, DateTimeOffset now)
    {
        if (state == TransferState.Rejected)
        {
            transfer.RequestAuthorisation(now);
            transfer.RejectAuthorisation(now);
            return;
        }

        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        switch (state)
        {
            case TransferState.PendingFraudScreening:
                transfer.BeginFraudScreening(now);
                break;
            case TransferState.BalanceReserved:
                transfer.BeginFraudScreening(now);
                transfer.RequestBalanceReservation(now);
                transfer.MarkBalanceReserved(now);
                break;
            case TransferState.ManualReviewRequired:
                transfer.BeginFraudScreening(now);
                transfer.EscalateFraudToManualReview(now);
                break;
            case TransferState.SubmissionStatusUnknown:
                transfer.BeginFraudScreening(now);
                transfer.RequestBalanceReservation(now);
                transfer.MarkBalanceReserved(now);
                transfer.BeginExternalSubmission(now);
                transfer.MarkSubmissionStatusUnknown(now);
                break;
            case TransferState.Completed:
                transfer.BeginFraudScreening(now);
                transfer.RequestBalanceReservation(now);
                transfer.MarkBalanceReserved(now);
                transfer.BeginExternalSubmission(now);
                transfer.MarkSettlementPending(now);
                transfer.CompleteSettlement(now);
                break;
            case TransferState.FraudRejected:
                transfer.BeginFraudScreening(now);
                transfer.RejectForFraud(now);
                break;
            default:
                throw new InvalidOperationException($"Unsupported seed state '{state}'.");
        }
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedCode,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(expectedCode, problem!.Extensions["code"]?.ToString());
    }

    private static HttpRequestMessage StuckTransfersRequest(
        Guid? correlationId = null,
        int? maxResults = null)
    {
        var path = maxResults is null
            ? "/api/operations/stuck-transfers"
            : $"/api/operations/stuck-transfers?maxResults={maxResults.Value}";
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId.Value.ToString("D"));
        }

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

    private sealed class StuckFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly TimeProvider _clock;

        private StuckFactory(string connectionString, TimeProvider clock)
        {
            _connectionString = connectionString;
            _clock = clock;
        }

        public static Task<StuckFactory> CreateAsync(string connectionString, TimeProvider clock) =>
            Task.FromResult(new StuckFactory(connectionString, clock));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Database", _connectionString);
            TestSecurityDefaults.ConfigureWebHost(builder);
            builder.UseSetting("TransferManagement:StuckTransfers:StateAgeThresholdSeconds", ThresholdSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("TransferManagement:StuckTransfers:MaxResults", "50");
            builder.UseSetting("TransferManagement:Reconciliation:EscalationAttemptThreshold", "1");
            builder.UseSetting("TransferManagement:Reconciliation:RetryDelaySeconds", "5");
            builder.UseSetting("TransferManagement:Reconciliation:BatchSize", "20");
            builder.UseSetting("TransferManagement:Reconciliation:LeaseDurationSeconds", "30");
            builder.UseSetting("TransferManagement:Reconciliation:PollIntervalMilliseconds", "1000");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.RemoveAll<IPaymentNetworkGateway>();
                services.AddSingleton<IPaymentNetworkGateway>(new RecordingGateway
                {
                    SubmissionResult = PaymentSubmissionResult.Timeout,
                    StatusResult = PaymentStatusResult.Unknown
                });
                services.RemoveHostedWorkers();
            });
        }
    }
}

[CollectionDefinition("PostgreSQL stuck transfers", DisableParallelization = true)]
public sealed class PostgreSqlStuckTransfersGroup;
