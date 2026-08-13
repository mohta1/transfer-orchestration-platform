using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.TransferManagement.Application.FraudScreening;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;
using TransferOrchestration.AuditOperations.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL submission API")]
public sealed class TransferSubmissionApiTests
{
    private static readonly Guid Source = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Destination = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SuccessfulSubmissionPersistsOneTransferAndProcessAndPropagatesCorrelation()
    {
        var correlation = Guid.NewGuid();
        await using var factory = await SubmissionFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = Request("success", Payload(), correlation);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Response>();
        Assert.NotNull(body);
        Assert.Equal("PendingFraudScreening", body.State);
        Assert.Equal(correlation, body.CorrelationId);
        Assert.Equal(correlation.ToString("D"), response.Headers.GetValues("X-Correlation-ID").Single());

        await using (var dispatchScope = factory.Services.CreateAsyncScope())
        {
            await FraudScreeningTestSupport.DispatchDueFraudScreeningAsync(dispatchScope.ServiceProvider);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var transfer = await context.Transfers.SingleAsync();
        var process = await context.TransferProcessStates.SingleAsync();
        Assert.Equal(TransferState.PendingBalanceReservation, transfer.State);
        Assert.Equal(TransferProcessAction.ReserveBalance, process.NextAction);
        Assert.Equal(correlation, process.CorrelationId);
        Assert.Equal(1, await context.IdempotencyRecords.CountAsync());
    }

    [Fact]
    public async Task MissingIdempotencyKeyIsRejectedWithoutTransfer()
    {
        await using var factory = await SubmissionFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers")
        {
            Content = JsonContent.Create(Payload())
        };
        request.AuthorizeAsCustomer(Source);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public async Task InvalidSubmissionIsRejectedWithoutTransfer(object payload)
    {
        await using var factory = await SubmissionFactory.CreateAsync();
        using var client = factory.CreateClient();
        using var request = Request(Guid.NewGuid().ToString(), payload);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await factory.TransferCountAsync());
    }

    public static TheoryData<object> InvalidPayloads => new()
    {
        Payload(amount: 0m),
        Payload(amount: 1.00001m),
        Payload(destination: Source),
        new { SourceAccountId = Source, DestinationAccountId = Destination, Amount = 10m, Currency = "GBP", TransferType = "1" },
        new { SourceAccountId = Source, DestinationAccountId = Destination, Amount = 10m, Currency = "GBP", TransferType = "2" }
    };

    [Fact]
    public async Task IdempotencyKeyHeaderContractRejectsInvalidValuesAndAllowsTwoHundredCharacters()
    {
        await using var factory = await SubmissionFactory.CreateAsync();
        using var client = factory.CreateClient();
        foreach (var key in new[] { " ", new string('x', 201) })
        {
            using var invalid = Request(key, Payload());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(invalid)).StatusCode);
        }
        using var multiple = Request("first", Payload());
        multiple.Headers.TryAddWithoutValidation("Idempotency-Key", "second");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(multiple)).StatusCode);
        using var valid = Request(new string('x', 200), Payload());
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(valid)).StatusCode);
    }

    [Fact]
    public async Task ValidationConflictProcessingAndRejectionEchoCurrentRequestCorrelation()
    {
        var correlation = Guid.NewGuid();
        await using var factory = await SubmissionFactory.CreateAsync(authorization: DecisionOutcome.Rejected);
        using var client = factory.CreateClient();
        using var rejected = Request("rejected-correlation", Payload(), correlation);
        var rejectedResponse = await client.SendAsync(rejected);
        Assert.Equal(correlation.ToString("D"), rejectedResponse.Headers.GetValues("X-Correlation-ID").Single());

        using var invalid = Request("invalid-correlation", Payload(amount: 0), correlation);
        var invalidResponse = await client.SendAsync(invalid);
        Assert.Equal(correlation.ToString("D"), invalidResponse.Headers.GetValues("X-Correlation-ID").Single());

        await factory.SendAsync("conflict-correlation", Payload());
        using var conflict = Request("conflict-correlation", Payload(amount: 11), correlation);
        var conflictResponse = await client.SendAsync(conflict);
        Assert.Equal(correlation.ToString("D"), conflictResponse.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task AuthorizationRejectionStopsDailyLimitAndFraud()
    {
        await using var factory = await SubmissionFactory.CreateAsync(authorization: DecisionOutcome.Rejected);
        var response = await factory.SendAsync("auth-rejected", Payload());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.DailyLimit.CallCount);
        Assert.Equal(0, factory.Fraud.CallCount);
        Assert.Equal(TransferState.Rejected, await factory.SingleTransferStateAsync());
    }

    [Fact]
    public async Task DailyLimitRejectionStopsFraud()
    {
        await using var factory = await SubmissionFactory.CreateAsync(dailyLimit: DecisionOutcome.Rejected);
        var response = await factory.SendAsync("limit-rejected", Payload());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, factory.DailyLimit.CallCount);
        Assert.Equal(0, factory.Fraud.CallCount);
        Assert.Equal(TransferState.Rejected, await factory.SingleTransferStateAsync());
    }

    [Fact]
    public async Task FraudRejectionCannotReachBalanceReservation()
    {
        await using var factory = await SubmissionFactory.CreateAsync(fraud: FraudScreeningResult.Rejected);
        var response = await factory.SendAsync("fraud-rejected", Payload());
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(0, factory.Fraud.CallCount);

        await factory.DispatchFraudAsync();

        Assert.Equal(1, factory.Fraud.CallCount);
        Assert.Equal(TransferState.FraudRejected, await factory.SingleTransferStateAsync());
        Assert.Equal(0, await factory.ReservationCountAsync());
    }

    [Fact]
    public async Task SameKeySamePayloadReplaysWithoutSideEffectsAndDifferentPayloadConflicts()
    {
        await using var factory = await SubmissionFactory.CreateAsync();
        var first = await factory.SendAsync("duplicate-key", Payload());
        var replay = await factory.SendAsync("duplicate-key", Payload());
        var conflict = await factory.SendAsync("duplicate-key", Payload(amount: 11m));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1, await factory.TransferCountAsync());
        Assert.Equal(1, await factory.ProcessCountAsync());
        Assert.Equal(1, factory.Authorization.CallCount);
        Assert.Equal(0, factory.Fraud.CallCount);
        await factory.DispatchFraudAsync();
        Assert.Equal(1, factory.Fraud.CallCount);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsCreateAtMostOneTransferAndProcess()
    {
        await using var factory = await SubmissionFactory.CreateAsync();
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => factory.SendAsync("concurrent-key", Payload())));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(1, await factory.TransferCountAsync());
        Assert.Equal(1, await factory.ProcessCountAsync());
    }

    [Fact]
    public async Task AuthorizationFailureLeavesOneRecoverableWorkflowAndRetryDoesNotDuplicateIt()
    {
        await using var factory = await SubmissionFactory.CreateAsync(throwAuthorization: true);
        var failedResponse = await factory.SendAsync("recoverable-failure", Payload());
        Assert.Equal(HttpStatusCode.InternalServerError, failedResponse.StatusCode);
        Assert.Equal(1, await factory.TransferCountAsync());
        Assert.Equal(1, await factory.ProcessCountAsync());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<ITransferProcessManager>();
            Assert.Single(await manager.GetDueAsync(DateTimeOffset.UtcNow.AddMinutes(1), 10, CancellationToken.None));
            var context = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
            var record = await context.IdempotencyRecords.SingleAsync();
            Assert.NotNull(record.TransferId);
        }

        var retry = await factory.SendAsync("recoverable-failure", Payload());
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(1, await factory.TransferCountAsync());
        Assert.Equal(1, await factory.ProcessCountAsync());
    }

    [Fact]
    public async Task CumulativeDailyUsageIsDurableSeparatedByOwnerAndUtcDayAndAtomicUnderConcurrency()
    {
        await using var factory = await SubmissionFactory.CreateAsync(useConfiguredDailyLimit: true);
        var day = new DateOnly(2026, 8, 11);
        Assert.Equal(DecisionOutcome.Approved, await factory.ConsumeAsync(Source, 6_000m, day));
        Assert.Equal(DecisionOutcome.Rejected, await factory.ConsumeAsync(Source, 6_000m, day));
        Assert.Equal(DecisionOutcome.Approved, await factory.ConsumeAsync(Guid.NewGuid(), 6_000m, day));
        Assert.Equal(DecisionOutcome.Approved, await factory.ConsumeAsync(Source, 6_000m, day.AddDays(1)));

        var concurrentOwner = Guid.NewGuid();
        var outcomes = await Task.WhenAll(
            factory.ConsumeAsync(concurrentOwner, 6_000m, day),
            factory.ConsumeAsync(concurrentOwner, 6_000m, day));
        Assert.Equal(1, outcomes.Count(outcome => outcome == DecisionOutcome.Approved));
        Assert.Equal(1, outcomes.Count(outcome => outcome == DecisionOutcome.Rejected));
    }

    [Fact]
    public async Task ConcurrentDailyLimitClaimsDoNotExceedMaximum()
    {
        await using var factory = await SubmissionFactory.CreateAsync(useConfiguredDailyLimit: true);

        var responses = await Task.WhenAll(
            factory.SendAsync("concurrent-limit-1", Payload(amount: 6_000m)),
            factory.SendAsync("concurrent-limit-2", Payload(amount: 6_000m)));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Accepted));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity));
        var accepted = responses.Single(response => response.StatusCode == HttpStatusCode.Accepted);
        var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.UnprocessableEntity);
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<Response>();
        Assert.Equal(nameof(TransferSubmissionOutcome.Accepted), acceptedBody?.Outcome);
        var rejectedProblem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(rejectedProblem);
        Assert.Equal("daily_limit_exceeded", rejectedProblem.Extensions["code"]?.ToString());
        Assert.Equal(6_000m, await factory.ConsumedAmountAsync(Source, "GBP"));
        Assert.Equal(1, await factory.DailyUsageCountAsync(Source, "GBP"));
    }

    [Fact]
    public async Task ConcurrentDailyLimitClaimsAccumulateWhenBothFit()
    {
        await using var factory = await SubmissionFactory.CreateAsync(useConfiguredDailyLimit: true);

        var responses = await Task.WhenAll(
            factory.SendAsync("concurrent-fit-1", Payload(amount: 4_000m)),
            factory.SendAsync("concurrent-fit-2", Payload(amount: 4_000m)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(8_000m, await factory.ConsumedAmountAsync(Source, "GBP"));
        Assert.Equal(1, await factory.DailyUsageCountAsync(Source, "GBP"));
    }

    [Fact]
    public async Task CumulativeDailyLimitRejectionPreventsFraud()
    {
        await using var factory = await SubmissionFactory.CreateAsync(useConfiguredDailyLimit: true);
        Assert.Equal(HttpStatusCode.Accepted, (await factory.SendAsync("daily-first", Payload(amount: 6_000m))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await factory.SendAsync("daily-second", Payload(amount: 6_000m))).StatusCode);
        Assert.Equal(0, factory.Fraud.CallCount);
        await factory.DispatchFraudAsync();
        Assert.Equal(1, factory.Fraud.CallCount);
    }

    [Fact]
    public async Task SameKeyReplayDuringPendingFraudScreeningCreatesNoDuplicateWork()
    {
        await using var factory = await SubmissionFactory.CreateAsync();
        await factory.SendAsync("pending-fraud-replay", Payload());
        await factory.SendAsync("pending-fraud-replay", Payload());
        Assert.Equal(1, await factory.TransferCountAsync());
        Assert.Equal(1, await factory.ProcessCountAsync());
        Assert.Equal(0, factory.Fraud.CallCount);
        Assert.Equal(TransferState.PendingFraudScreening, await factory.SingleTransferStateAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var process = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
            .TransferProcessStates.SingleAsync();
        Assert.Equal(TransferProcessAction.RequestFraudScreening, process.NextAction);
    }

    private static object Payload(decimal amount = 10m, Guid? destination = null) => new
    {
        SourceAccountId = Source,
        DestinationAccountId = destination ?? Destination,
        Amount = amount,
        Currency = "GBP",
        TransferType = "DomesticInterbank"
    };

    private static HttpRequestMessage Request(string key, object payload, Guid? correlation = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(payload) };
        request.Headers.Add("Idempotency-Key", key);
        request.AuthorizeAsCustomer(Source);
        if (correlation is not null) request.Headers.Add("X-Correlation-ID", correlation.Value.ToString("D"));
        return request;
    }

    private sealed record Response(Guid? TransferId, Guid? CorrelationId, string? State, string Outcome);

    private sealed class SubmissionFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        public CountingAuthorization Authorization { get; }
        public CountingDailyLimit DailyLimit { get; }
        public CountingFraud Fraud { get; }

        private readonly bool _useConfiguredDailyLimit;

        private SubmissionFactory(string connectionString, DecisionOutcome authorization, DecisionOutcome dailyLimit, FraudScreeningResult fraud, bool throwAuthorization, bool useConfiguredDailyLimit)
        {
            _connectionString = connectionString;
            _useConfiguredDailyLimit = useConfiguredDailyLimit;
            Authorization = new CountingAuthorization(authorization, throwAuthorization);
            DailyLimit = new CountingDailyLimit(dailyLimit);
            Fraud = new CountingFraud(fraud);
        }

        public static async Task<SubmissionFactory> CreateAsync(
            DecisionOutcome authorization = DecisionOutcome.Approved,
            DecisionOutcome dailyLimit = DecisionOutcome.Approved,
            FraudScreeningResult fraud = FraudScreeningResult.Approved,
            bool throwAuthorization = false,
            bool useConfiguredDailyLimit = false)
        {
            var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Destructive PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP SCHEMA IF EXISTS audit_operations CASCADE; DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
            await command.ExecuteNonQueryAsync();
            var factory = new SubmissionFactory(connectionString, authorization, dailyLimit, fraud, throwAuthorization, useConfiguredDailyLimit);
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerAuthorization>();
                if (!_useConfiguredDailyLimit) services.RemoveAll<IDailyTransferLimit>();
                services.RemoveAll<IFraudScreening>();
                services.AddSingleton<ICustomerAuthorization>(Authorization);
                if (!_useConfiguredDailyLimit) services.AddSingleton<IDailyTransferLimit>(DailyLimit);
                services.AddSingleton<IFraudScreening>(Fraud);
            });
        }

        public async Task<HttpResponseMessage> SendAsync(string key, object payload)
        {
            using var client = CreateClient();
            using var request = Request(key, payload);
            return await client.SendAsync(request);
        }

        public async Task<int> TransferCountAsync() => await WithContext(context => context.Transfers.CountAsync());
        public async Task<int> ProcessCountAsync() => await WithContext(context => context.TransferProcessStates.CountAsync());
        public async Task<TransferState> SingleTransferStateAsync() => await WithContext(async context => (await context.Transfers.SingleAsync()).State);
        public async Task<decimal> ConsumedAmountAsync(Guid sourceAccountId, string currency) =>
            await WithContext(async context => (await context.DailyTransferUsages.SingleAsync(
                usage => usage.SourceAccountId == sourceAccountId && usage.Currency == currency)).ConsumedAmount);
        public async Task<int> DailyUsageCountAsync(Guid sourceAccountId, string currency) =>
            await WithContext(context => context.DailyTransferUsages.CountAsync(
                usage => usage.SourceAccountId == sourceAccountId && usage.Currency == currency));

        public async Task<DecisionOutcome> ConsumeAsync(Guid sourceAccountId, decimal amount, DateOnly day)
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IDailyTransferLimit>()
                .TryConsumeAsync(sourceAccountId, amount, "GBP", day, CancellationToken.None);
        }

        public async Task DispatchFraudAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            await FraudScreeningTestSupport.DispatchDueFraudScreeningAsync(scope.ServiceProvider);
        }

        public async Task<int> ReservationCountAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var transferId = await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>()
                .Transfers.Select(transfer => transfer.Id.Value)
                .SingleAsync();
            return await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>()
                .Set<BalanceReservation>()
                .CountAsync(reservation => reservation.TransferId == transferId);
        }

        private async Task<T> WithContext<T>(Func<TransferManagementDbContext, Task<T>> query)
        {
            await using var scope = Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>());
        }
    }

    private sealed class CountingAuthorization(DecisionOutcome outcome, bool throwOnCall = false) : ICustomerAuthorization
    {
        public int CallCount { get; private set; }
        public Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken)
        {
            CallCount++;
            if (throwOnCall) throw new InvalidOperationException("Injected authorization failure.");
            return Task.FromResult(outcome);
        }
    }

    private sealed class CountingDailyLimit(DecisionOutcome outcome) : IDailyTransferLimit
    {
        public int CallCount { get; private set; }
        public Task<DecisionOutcome> TryConsumeAsync(Guid sourceAccountId, decimal amount, string currency, DateOnly utcDay, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class CountingFraud(FraudScreeningResult outcome) : IFraudScreening
    {
        public int CallCount { get; private set; }

        public Task<FraudScreeningResult> ScreenAsync(FraudScreeningRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outcome);
        }
    }
}

[CollectionDefinition("PostgreSQL submission API", DisableParallelization = true)]
public sealed class PostgreSqlSubmissionApiGroup;
