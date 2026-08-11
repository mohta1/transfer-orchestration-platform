using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TransferOrchestration.BuildingBlocks.Domain;

namespace TransferOrchestration.TransferManagement.Application.Idempotency;

internal static class TransferSubmissionFingerprint
{
    public static string Create(TransferSubmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        MonetaryAmountGuard.EnsureRepresentable(request.Amount, "Transfer amount");

        var currency = request.Currency?.Trim().ToUpperInvariant()
            ?? throw new ArgumentException("Currency is required.", nameof(request));
        var canonical = string.Join(
            '|',
            Field("sourceAccountId", request.SourceAccountId.ToString("D", CultureInfo.InvariantCulture)),
            Field("destinationAccountId", request.DestinationAccountId.ToString("D", CultureInfo.InvariantCulture)),
            Field("amount", request.Amount.ToString("0.0000", CultureInfo.InvariantCulture)),
            Field("currency", currency),
            Field("type", request.Type.ToString()));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Field(string name, string value) =>
        string.Create(CultureInfo.InvariantCulture, $"{name}:{value.Length}:{value}");
}
