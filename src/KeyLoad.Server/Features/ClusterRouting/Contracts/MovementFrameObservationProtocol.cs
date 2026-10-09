namespace KeyLoad.Server.Features.ClusterRouting;

internal static class MovementFrameObservationProtocol
{
    internal const int Version = 1;
    internal const int JsonDepth = 4;
    internal const int PrefixStep = 1;
    internal const string OwnerKind = "movement-frame-owner";
    internal const string SelectionKind = "movement-frame-selection";
    internal const string ObservationKind = "movement-frame-encoded-refusal";
    internal const string OwnerFile = "owner.json";
    internal const string SelectionFile = "selection.json";
    internal const string ObservationFile = "encoded-refusal.json";
    internal const string ConfigurationSection = "KeyLoad:MovementFrameObservation";
    internal const string FixedRoot = "/movement-frame-observation";
    internal const string Invalid = "The private movement frame observation is invalid.";
}
