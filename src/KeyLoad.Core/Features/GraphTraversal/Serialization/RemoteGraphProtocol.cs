namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class RemoteGraphProtocol
{
    internal const int Version = 1;
    internal const int EmptyCount = 0;
    internal const int ItemSeparatorBytes = 1;
    internal const long MinimumReadGeneration = 0;
    internal const long MinimumPolicyEpoch = 0;
    internal const long MinimumSchemaVersion = 1;
    internal const string IntentFamily = "graph-remote-intent-v1";
    internal const string ReceiverFamily = "graph-remote-receiver-v1";
    internal const string CapacityFamily = "graph-remote-capacity-v1";
    internal const string CompletionFamily = "graph-remote-completion-v1";
    internal const string Invalid = "The remote graph proof is invalid.";
    internal const string MissingAuthority = "The remote graph owner is unavailable.";
    internal const string MissingApplied = "The applied command position is unavailable.";
    internal const string PreparationPurpose = "keyload-remote-graph-preparation-v1";
    internal const string ReceiverPurpose = "keyload-remote-graph-receiver-v1";
    internal const string CompletionPurpose = "keyload-remote-graph-completion-v1";
    internal const string EventualProjection = "remote-owner-eventual-v1";
}
