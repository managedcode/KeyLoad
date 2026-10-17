namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionCachedEmptyPolicyState(SubscriptionCachedEmptyPublicState Original,
    ReceiveSubscriptionRequest Denied, string RevokedProblem, string EmptyPolicyProblem, string DeniedPolicyProblem)
{
    internal ReceiveSubscriptionRequest? Claim
    {
        get; set;
    }
    internal SubscriptionDeliveryCommand? Ack
    {
        get; set;
    }
    internal CommitReceipt? Acknowledged
    {
        get; set;
    }
}
