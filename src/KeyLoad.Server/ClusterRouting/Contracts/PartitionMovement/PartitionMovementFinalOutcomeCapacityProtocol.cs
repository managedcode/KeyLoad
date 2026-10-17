namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementFinalOutcomeCapacityProtocol
{
    internal const string Exceeded = "The final Install outcome exceeds its reserved transport capacity.";
    internal const int AuthorizationGuidFields = 2;
    internal const int FailedResultReferenceFields = 3;
    internal const int AuthorizationPhaseSlots = 2;
    internal const int ParentPhaseSlots = 4;
    internal const int SinglePhaseSlot = 1;
}
