using TransferOrchestration.PaymentNetwork.Contracts;

namespace TransferOrchestration.PaymentNetwork.Application;

internal interface IPaymentNetworkProvider
{
    Task<ProviderSubmissionOutcome> SubmitAsync(ProviderSubmission submission, CancellationToken cancellationToken);
    Task<ProviderStatusOutcome> GetStatusAsync(string reference, CancellationToken cancellationToken);
}

internal sealed record ProviderSubmission(
    string Reference,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency);

internal enum ProviderSubmissionOutcome { Accepted, Rejected, TimedOut }
internal enum ProviderStatusOutcome { Accepted, Rejected, Settled, Unknown }

internal sealed class PaymentNetworkGateway(IPaymentNetworkProvider provider) : IPaymentNetworkGateway
{
    public NetworkSubmissionReference CreateSubmissionReference(Guid transferId)
    {
        if (transferId == Guid.Empty)
        {
            throw new ArgumentException("Transfer identifier is required.", nameof(transferId));
        }

        return new NetworkSubmissionReference($"TOP-{transferId:N}".ToUpperInvariant());
    }

    public async Task<PaymentSubmissionResult> SubmitAsync(PaymentSubmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = await provider.SubmitAsync(
            new ProviderSubmission(request.Reference.Value, request.SourceAccountId, request.DestinationAccountId, request.Amount, request.Currency),
            cancellationToken);
        return outcome switch
        {
            ProviderSubmissionOutcome.Accepted => PaymentSubmissionResult.Accepted,
            ProviderSubmissionOutcome.Rejected => PaymentSubmissionResult.Rejected,
            ProviderSubmissionOutcome.TimedOut => PaymentSubmissionResult.Timeout,
            _ => throw new InvalidOperationException("Unsupported payment provider submission outcome.")
        };
    }

    public async Task<PaymentStatusResult> GetStatusAsync(NetworkSubmissionReference reference, CancellationToken cancellationToken) =>
        (await provider.GetStatusAsync(reference.Value, cancellationToken)) switch
        {
            ProviderStatusOutcome.Accepted => PaymentStatusResult.Accepted,
            ProviderStatusOutcome.Rejected => PaymentStatusResult.Rejected,
            ProviderStatusOutcome.Settled => PaymentStatusResult.Settled,
            ProviderStatusOutcome.Unknown => PaymentStatusResult.Unknown,
            _ => throw new InvalidOperationException("Unsupported payment provider status outcome.")
        };
}

internal sealed class DefaultPaymentNetworkProvider : IPaymentNetworkProvider
{
    public Task<ProviderSubmissionOutcome> SubmitAsync(ProviderSubmission submission, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderSubmissionOutcome.Accepted);

    public Task<ProviderStatusOutcome> GetStatusAsync(string reference, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderStatusOutcome.Unknown);
}
