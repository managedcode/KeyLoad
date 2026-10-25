
namespace KeyLoad.ClusterTests.Messaging;

internal sealed record SubscriptionInboxPublicState(ReceiveSubscriptionRequest First, ReceiveSubscriptionResult FirstResult,
    SubscriptionProcessingRequest Processing, SubscriptionProcessingResult ProcessingResult,
    ReceiveSubscriptionRequest Current, ReceiveSubscriptionResult CurrentResult,
    SubscriptionProcessingRequest Conflict, string ConflictProblem, EventSourcePage Source)
{
    internal SubscriptionProcessingRequest? FailedProcessing
    {
        get; init;
    }
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt)> Acknowledgements { get; } = [];
    internal SubscriptionProcessingRequest? Repair
    {
        get; set;
    }
    internal SubscriptionProcessingResult? Repaired
    {
        get; set;
    }
}
