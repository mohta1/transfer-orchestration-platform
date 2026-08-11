namespace TransferOrchestration.PaymentNetwork.Contracts;

public interface IPaymentNetworkGateway
{
    NetworkSubmissionReference CreateSubmissionReference(Guid transferId);

    Task<PaymentSubmissionResult> SubmitAsync(
        PaymentSubmissionRequest request,
        CancellationToken cancellationToken);

    Task<PaymentStatusResult> GetStatusAsync(
        NetworkSubmissionReference reference,
        CancellationToken cancellationToken);
}

public sealed record NetworkSubmissionReference
{
    public const int MaximumLength = 80;

    public NetworkSubmissionReference(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record PaymentSubmissionRequest(
    Guid TransferId,
    NetworkSubmissionReference Reference,
    Guid SourceAccountId,
    Guid DestinationAccountId,
    decimal Amount,
    string Currency);

public enum PaymentSubmissionResult { Accepted, Rejected, Timeout }

public enum PaymentStatusResult { Accepted, Rejected, Settled, Unknown }
