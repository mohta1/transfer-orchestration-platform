using TransferOrchestration.PaymentNetwork.Application;
using TransferOrchestration.PaymentNetwork.Contracts;

namespace TransferOrchestration.IntegrationTests;

public sealed class PaymentNetworkAclTests
{
    [Fact]
    public void SubmissionReferenceIsStableAndUniquePerTransfer()
    {
        var gateway = new PaymentNetworkGateway(new RecordingProvider());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        Assert.Equal(gateway.CreateSubmissionReference(firstId), gateway.CreateSubmissionReference(firstId));
        Assert.NotEqual(gateway.CreateSubmissionReference(firstId), gateway.CreateSubmissionReference(secondId));
    }

    [Fact]
    public async Task SubmissionOutcomesAreMappedWithoutProviderDetails()
    {
        var outcomes = new[]
        {
            (ProviderSubmissionOutcome.Accepted, PaymentSubmissionResult.Accepted),
            (ProviderSubmissionOutcome.Rejected, PaymentSubmissionResult.Rejected),
            (ProviderSubmissionOutcome.TimedOut, PaymentSubmissionResult.Timeout)
        };
        foreach (var (providerOutcome, expected) in outcomes)
        {
            var provider = new RecordingProvider { SubmissionOutcome = providerOutcome };
            var gateway = new PaymentNetworkGateway(provider);
            var transferId = Guid.NewGuid();
            var reference = gateway.CreateSubmissionReference(transferId);
            var request = new PaymentSubmissionRequest(
                transferId, reference, Guid.NewGuid(), Guid.NewGuid(), 125.50m, "GBP");

            Assert.Equal(expected, await gateway.SubmitAsync(request, CancellationToken.None));
            var submitted = Assert.Single(provider.Submissions);
            Assert.Equal(reference.Value, submitted.Reference);
            Assert.Equal(request.SourceAccountId, submitted.SourceAccountId);
            Assert.Equal(request.DestinationAccountId, submitted.DestinationAccountId);
            Assert.Equal(request.Amount, submitted.Amount);
            Assert.Equal(request.Currency, submitted.Currency);
        }
    }

    [Fact]
    public async Task StatusEnquiryUsesTheExactSubmissionReference()
    {
        var provider = new RecordingProvider();
        var gateway = new PaymentNetworkGateway(provider);
        var reference = gateway.CreateSubmissionReference(Guid.NewGuid());

        Assert.Equal(PaymentStatusResult.Unknown, await gateway.GetStatusAsync(reference, CancellationToken.None));
        Assert.Equal(reference.Value, Assert.Single(provider.Enquiries));
    }

    private sealed class RecordingProvider : IPaymentNetworkProvider
    {
        public ProviderSubmissionOutcome SubmissionOutcome { get; init; } = ProviderSubmissionOutcome.Accepted;
        public List<ProviderSubmission> Submissions { get; } = [];
        public List<string> Enquiries { get; } = [];

        public Task<ProviderSubmissionOutcome> SubmitAsync(ProviderSubmission submission, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Submissions.Add(submission);
            return Task.FromResult(SubmissionOutcome);
        }

        public Task<ProviderStatusOutcome> GetStatusAsync(string reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Enquiries.Add(reference);
            return Task.FromResult(ProviderStatusOutcome.Unknown);
        }
    }
}
