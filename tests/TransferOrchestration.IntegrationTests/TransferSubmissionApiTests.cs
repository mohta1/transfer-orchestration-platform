using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.TransferManagement.Application.Idempotency;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Application.Submission;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

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
        Assert.Equal("PendingBalanceReservation", body.State);
        Assert.Equal(correlation, body.CorrelationId);
        Assert.Equal(correlation.ToString("D"), response.Headers.GetValues("X-Correlation-ID").Single());
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
        var response = await client.PostAsJsonAsync("/api/transfers", Payload());
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
        Payload(destination: Source)
    };

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
        await using var factory = await SubmissionFactory.CreateAsync(fraud: DecisionOutcome.Rejected);
        var response = await factory.SendAsync("fraud-rejected", Payload());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(1, factory.Fraud.CallCount);
        Assert.Equal(TransferState.FraudRejected, await factory.SingleTransferStateAsync());
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

        private SubmissionFactory(string connectionString, DecisionOutcome authorization, DecisionOutcome dailyLimit, DecisionOutcome fraud)
        {
            _connectionString = connectionString;
            Authorization = new CountingAuthorization(authorization);
            DailyLimit = new CountingDailyLimit(dailyLimit);
            Fraud = new CountingFraud(fraud);
        }

        public static async Task<SubmissionFactory> CreateAsync(
            DecisionOutcome authorization = DecisionOutcome.Approved,
            DecisionOutcome dailyLimit = DecisionOutcome.Approved,
            DecisionOutcome fraud = DecisionOutcome.Approved)
        {
            var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
                ?? throw new InvalidOperationException("Destructive PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
            await command.ExecuteNonQueryAsync();
            var factory = new SubmissionFactory(connectionString, authorization, dailyLimit, fraud);
            _ = factory.Services;
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
            return factory;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Database", _connectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICustomerAuthorization>();
                services.RemoveAll<IDailyTransferLimit>();
                services.RemoveAll<IFraudScreening>();
                services.AddSingleton<ICustomerAuthorization>(Authorization);
                services.AddSingleton<IDailyTransferLimit>(DailyLimit);
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

        private async Task<T> WithContext<T>(Func<TransferManagementDbContext, Task<T>> query)
        {
            await using var scope = Services.CreateAsyncScope();
            return await query(scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>());
        }
    }

    private sealed class CountingAuthorization(DecisionOutcome outcome) : ICustomerAuthorization
    {
        public int CallCount { get; private set; }
        public Task<DecisionOutcome> IsAuthorizedAsync(Guid sourceAccountId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outcome);
        }
    }

    private sealed class CountingDailyLimit(DecisionOutcome outcome) : IDailyTransferLimit
    {
        public int CallCount { get; private set; }
        public DecisionOutcome Evaluate(decimal amount, string currency) { CallCount++; return outcome; }
    }

    private sealed class CountingFraud(DecisionOutcome outcome) : IFraudScreening
    {
        public int CallCount { get; private set; }
        public Task<DecisionOutcome> ScreenAsync(TransferSubmissionRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(outcome);
        }
    }
}

[CollectionDefinition("PostgreSQL submission API", DisableParallelization = true)]
public sealed class PostgreSqlSubmissionApiGroup;
