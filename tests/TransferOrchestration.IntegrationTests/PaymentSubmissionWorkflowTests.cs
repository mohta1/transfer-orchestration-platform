using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TransferOrchestration.AccountBalance;
using TransferOrchestration.AccountBalance.Domain.Accounts;
using TransferOrchestration.AccountBalance.Infrastructure.Persistence;
using TransferOrchestration.PaymentNetwork.Contracts;
using TransferOrchestration.TransferManagement;
using TransferOrchestration.TransferManagement.Application.PaymentSubmission;
using TransferOrchestration.TransferManagement.Application.ProcessManagement;
using TransferOrchestration.TransferManagement.Domain.Transfers;
using TransferOrchestration.TransferManagement.Infrastructure.Persistence;

namespace TransferOrchestration.IntegrationTests;

[Collection("PostgreSQL account reservation")]
public sealed class PaymentSubmissionWorkflowTests : IAsyncLifetime
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_DATABASE_CONNECTION_STRING")
        ?? throw new InvalidOperationException("Destructive PostgreSQL tests require TEST_DATABASE_CONNECTION_STRING.");

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS transfer_management CASCADE; DROP SCHEMA IF EXISTS account_balance CASCADE;";
        await command.ExecuteNonQueryAsync();
        await using var provider = CreateProvider(new RecordingGateway());
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AcceptedIsSettlementPendingAndCannotBeSubmittedAgain()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Accepted };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        Assert.Equal(0, await DispatchPaymentAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.SettlementPending, snapshot.TransferState);
        Assert.Equal(TransferProcessStatus.Waiting, snapshot.ProcessStatus);
        Assert.Equal(TransferProcessAction.None, snapshot.NextAction);
        Assert.Single(gateway.SubmitCalls);
        Assert.Equal(snapshot.Reference, gateway.SubmitCalls.Single().Reference.Value);
        AssertReservationActive(snapshot);
    }

    [Fact]
    public async Task RejectionRequestsReleaseWithoutSubmittingTwice()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Rejected };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        Assert.Equal(0, await DispatchPaymentAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.Rejected, snapshot.TransferState);
        Assert.Equal(TransferProcessAction.ReleaseReservation, snapshot.NextAction);
        Assert.Single(gateway.SubmitCalls);
        AssertReservationActive(snapshot);
    }

    [Fact]
    public async Task TimeoutPersistsUnknownAndRestartUsesSameReferenceWithoutResubmit()
    {
        var gateway = new RecordingGateway { SubmissionResult = PaymentSubmissionResult.Timeout };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));
        var first = await SnapshotAsync(transferId);
        AssertUnknown(first, gateway);

        await using (var restarted = CreateProvider(gateway))
        await using (var scope = restarted.CreateAsyncScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>()
                .DispatchDueAsync(CancellationToken.None));
            var reference = new NetworkSubmissionReference(first.Reference!);
            await scope.ServiceProvider.GetRequiredService<IPaymentNetworkGateway>()
                .GetStatusAsync(reference, CancellationToken.None);
        }

        Assert.Single(gateway.SubmitCalls);
        Assert.Equal(first.Reference, Assert.Single(gateway.StatusCalls).Value);
        AssertUnknown(await SnapshotAsync(transferId), gateway);
    }

    [Fact]
    public async Task ThrownAmbiguousProviderExceptionIsDurablyUnknownAfterFreshReload()
    {
        var gateway = new RecordingGateway { ThrowAmbiguousException = true };
        var transferId = await SeedReservedTransferAsync(TransferType.DomesticInterbank, gateway);

        Assert.Equal(1, await DispatchPaymentAsync(gateway));

        // SnapshotAsync creates a new provider and DbContext, proving the outcome was
        // committed before the dispatcher returned rather than retained in tracking.
        var snapshot = await SnapshotAsync(transferId);
        AssertUnknown(snapshot, gateway);
        Assert.Equal(snapshot.Reference, gateway.SubmitCalls.Single().Reference.Value);
    }

    [Fact]
    public async Task InternalBankNeverRoutesToOrCallsPaymentNetwork()
    {
        var gateway = new RecordingGateway();
        var transferId = await SeedReservedTransferAsync(TransferType.InternalBank, gateway);

        Assert.Equal(0, await DispatchPaymentAsync(gateway));

        var snapshot = await SnapshotAsync(transferId);
        Assert.Equal(TransferState.BalanceReserved, snapshot.TransferState);
        Assert.Equal(TransferProcessStatus.Waiting, snapshot.ProcessStatus);
        Assert.Equal(TransferProcessAction.None, snapshot.NextAction);
        Assert.Null(snapshot.Reference);
        Assert.Empty(gateway.SubmitCalls);
        Assert.Empty(gateway.StatusCalls);
        AssertReservationActive(snapshot);
    }

    private async Task<TransferId> SeedReservedTransferAsync(TransferType type, RecordingGateway gateway)
    {
        var accountId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var transfer = Transfer.Create(accountId, Guid.NewGuid(), 100m, "GBP", type, now);
        transfer.Submit(now);
        transfer.RequestAuthorisation(now);
        transfer.Authorise(now);
        transfer.BeginFraudScreening(now);
        transfer.RequestBalanceReservation(now);
        var process = TransferProcessState.Create(transfer.Id, Guid.NewGuid(), now);
        process.Schedule(TransferProcessAction.ReserveBalance, now, now);

        await using var provider = CreateProvider(gateway);
        await using var scope = provider.CreateAsyncScope();
        var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        accountContext.Accounts.Add(Account.Create(accountId, "GBP", 500m, AccountStatus.Active));
        await accountContext.SaveChangesAsync();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        transferContext.AddRange(transfer, process);
        await transferContext.SaveChangesAsync();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ITransferProcessDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None));
        return transfer.Id;
    }

    private async Task<int> DispatchPaymentAsync(RecordingGateway gateway)
    {
        await using var provider = CreateProvider(gateway);
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPaymentSubmissionDueWorkDispatcher>()
            .DispatchDueAsync(CancellationToken.None);
    }

    private async Task<WorkflowSnapshot> SnapshotAsync(TransferId transferId)
    {
        await using var provider = CreateProvider(new RecordingGateway());
        await using var scope = provider.CreateAsyncScope();
        var transferContext = scope.ServiceProvider.GetRequiredService<TransferManagementDbContext>();
        var transfer = await transferContext.Transfers.AsNoTracking().SingleAsync(x => x.Id == transferId);
        var process = await transferContext.TransferProcessStates.AsNoTracking().SingleAsync(x => x.TransferId == transferId);
        var accountContext = scope.ServiceProvider.GetRequiredService<AccountBalanceDbContext>();
        var account = await accountContext.Accounts.Include(x => x.Reservations).AsNoTracking()
            .SingleAsync(x => x.Id == new AccountId(transfer.SourceAccountId));
        var reservation = Assert.Single(account.Reservations);
        return new WorkflowSnapshot(
            transfer.State, process.Status, process.NextAction, process.NetworkSubmissionReference,
            account.AvailableBalance, account.ReservedBalance, reservation.Status);
    }

    private ServiceProvider CreateProvider(RecordingGateway gateway)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAccountBalanceModule(_connectionString);
        services.AddTransferManagementModule(_connectionString, configuration);
        services.Replace(ServiceDescriptor.Singleton<IPaymentNetworkGateway>(gateway));
        return services.BuildServiceProvider();
    }

    private static void AssertUnknown(WorkflowSnapshot snapshot, RecordingGateway gateway)
    {
        Assert.Equal(TransferState.SubmissionStatusUnknown, snapshot.TransferState);
        Assert.Equal(TransferProcessStatus.Active, snapshot.ProcessStatus);
        Assert.Equal(TransferProcessAction.EnquirePaymentStatus, snapshot.NextAction);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Reference));
        Assert.Single(gateway.SubmitCalls);
        AssertReservationActive(snapshot);
    }

    private static void AssertReservationActive(WorkflowSnapshot snapshot)
    {
        Assert.Equal(400m, snapshot.Available);
        Assert.Equal(100m, snapshot.Reserved);
        Assert.Equal(BalanceReservationStatus.Active, snapshot.ReservationStatus);
    }

    private sealed class RecordingGateway : IPaymentNetworkGateway
    {
        public PaymentSubmissionResult SubmissionResult { get; init; } = PaymentSubmissionResult.Accepted;
        public bool ThrowAmbiguousException { get; init; }
        public List<PaymentSubmissionRequest> SubmitCalls { get; } = [];
        public List<NetworkSubmissionReference> StatusCalls { get; } = [];

        public NetworkSubmissionReference CreateSubmissionReference(Guid transferId) =>
            new($"TEST-{transferId:N}".ToUpperInvariant());

        public Task<PaymentSubmissionResult> SubmitAsync(PaymentSubmissionRequest request, CancellationToken cancellationToken)
        {
            SubmitCalls.Add(request);
            if (ThrowAmbiguousException)
            {
                throw new TimeoutException("Provider outcome is ambiguous.");
            }

            return Task.FromResult(SubmissionResult);
        }

        public Task<PaymentStatusResult> GetStatusAsync(NetworkSubmissionReference reference, CancellationToken cancellationToken)
        {
            StatusCalls.Add(reference);
            return Task.FromResult(PaymentStatusResult.Unknown);
        }
    }

    private sealed record WorkflowSnapshot(
        TransferState TransferState,
        TransferProcessStatus ProcessStatus,
        TransferProcessAction NextAction,
        string? Reference,
        decimal Available,
        decimal Reserved,
        BalanceReservationStatus ReservationStatus);
}
