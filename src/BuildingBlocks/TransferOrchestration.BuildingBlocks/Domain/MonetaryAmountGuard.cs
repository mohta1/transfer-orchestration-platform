namespace TransferOrchestration.BuildingBlocks.Domain;

public static class MonetaryAmountGuard
{
    private const decimal MaximumNumeric19Scale4 = 999_999_999_999_999.9999m;

    public static void EnsureRepresentable(decimal amount, string name)
    {
        if (decimal.Round(amount, 4) != amount
            || amount < -MaximumNumeric19Scale4
            || amount > MaximumNumeric19Scale4)
        {
            throw new DomainException(
                $"{name} must be exactly representable with precision 19 and scale 4.");
        }
    }
}
