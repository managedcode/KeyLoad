namespace KeyLoad.ClusterTests.Authorization;

internal sealed class EventingRequiredClaimClusterState(RequiredProcessingPolicyClusterState input)
{
    internal RequiredProcessingPolicyClusterState Input { get; } = input;
    internal ReceiveRequest Denied { get; } = new(Guid.NewGuid(), input.Lane);
    internal ReceiveRequest EpochProbe { get; } = new(Guid.NewGuid(), input.Lane, MaxBytes: 1);
    internal ReceiveRequest Claim { get; } = new(Guid.NewGuid(), input.Lane);
    internal ReceiveResult EpochReceipt { get; set; } = null!;
    internal ReceiveResult Claimed { get; set; } = null!;
    internal bool Completed
    {
        get; set;
    }
    internal ProcessingRequest Healthy { get; set; } = null!;
    internal CommitReceipt Receipt { get; set; } = null!;
    internal const string Handler = "throw new InvalidOperationException(\"HANDLER-INJECTION-CANARY\")";
}
