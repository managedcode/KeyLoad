
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record SubscriptionInboxPublicState(ReceiveSubscriptionRequest First, ReceiveSubscriptionResult FirstResult,
    SubscriptionProcessingRequest Processing, SubscriptionProcessingResult ProcessingResult,
    ReceiveSubscriptionRequest Current, ReceiveSubscriptionResult CurrentResult,
    SubscriptionProcessingRequest Conflict, string ConflictProblem, EventSourcePage Source)
{
    internal List<(SubscriptionDeliveryCommand Request, CommitReceipt Receipt)> Acknowledgements { get; } = [];
    internal SubscriptionProcessingRequest? Repair { get; set; }
    internal SubscriptionProcessingResult? Repaired { get; set; }
}
