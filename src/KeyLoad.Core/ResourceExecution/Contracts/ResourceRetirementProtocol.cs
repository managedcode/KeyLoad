namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Names the durable, forward-only resource retirement barrier protocol.</summary>
internal static class ResourceRetirementProtocol
{
    internal const int Version = 1;
    internal const int Empty = 0;
    internal const int First = 1;
    internal const int DigestCharacters = 64;
    internal const string Retiring = "The resource lifetime is retiring.";
    internal const string Invalid = "The resource retirement request is invalid.";
    internal const string Malformed = "The persisted resource retirement state is malformed.";
    internal const string Changed = "The resource retirement authority changed.";
    internal const string Missing = "The resource retirement progress is unavailable.";
    internal const string Incomplete = "The resource retirement owner barrier is incomplete.";
    internal const string Capacity = "The resource retirement state exceeds its native bound.";
    internal const string Administrator = "Resource retirement requires current cluster administration.";
}
