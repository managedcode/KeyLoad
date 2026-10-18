namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

internal static class PartitionMoveProtocol
{
    internal const int EmptyCount = 0;
    internal const string Invalid = "The partition movement state is invalid.";
    internal const string OwnerMismatch = "The partition movement owner no longer matches its admission.";
    internal const string Capacity = "The partition movement exceeds its admitted image capacity.";
    internal const string MissingAuthority = "The partition movement has no committed apply authority.";
}
