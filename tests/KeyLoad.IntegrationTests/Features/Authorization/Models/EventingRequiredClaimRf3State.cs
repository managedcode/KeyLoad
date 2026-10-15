namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class EventingRequiredClaimRf3State(RequiredProcessingPolicyRf3State input)
{
    internal RequiredProcessingPolicyRf3State Input { get; } = input;
    internal ReceiveRequest Denied { get; } = new(Guid.NewGuid(), input.Lane);
    internal bool Completed
    {
        get; set;
    }
    internal ProcessingRequest Healthy { get; set; } = null!;
    internal CommitReceipt Receipt { get; set; } = null!;
    internal const string Handler = "throw new InvalidOperationException(\"HANDLER-INJECTION-CANARY\")";
}
