using System.Collections.Immutable;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed class ControlledPartitionMovementNativeFailureRoot
{
    private const string FailureRootKey = "KeyLoad.ControlledMovement.NativeFailureRoot";
    private const int FirstOwner = 0;
    private ControlledPartitionMovementNativeFailureRoot(string root, ImmutableArray<string> nodes)
    {
        Root = root;
        Nodes = nodes;
    }
    internal string Root
    {
        get;
    }
    internal ImmutableArray<string> Nodes
    {
        get;
    }

    internal static ControlledPartitionMovementNativeFailureRoot Capture(ControlledPartitionMovementNativeHostSettings settings)
        => new(Path.GetFullPath(settings.Root), Enumerable.Range(FirstOwner,
            ControlledPartitionMovementNativeHostEndpoints.OwnerCount)
            .Select(index => Path.GetFullPath(settings.DirectoryPath(index))).ToImmutableArray());

    internal void Attach(Exception original)
    {
        if (original.Data.Contains(FailureRootKey))
        {
            throw new InvalidOperationException(nameof(ControlledPartitionMovementNativeFailureRoot));
        }
        original.Data.Add(FailureRootKey, this);
    }

    internal static ControlledPartitionMovementNativeFailureRoot? Find(Exception original)
    {
        if (original.Data[FailureRootKey] is ControlledPartitionMovementNativeFailureRoot retained)
        {
            return retained;
        }
        if (original is AggregateException aggregate)
        {
            foreach (var child in aggregate.InnerExceptions)
            {
                if (Find(child) is { } nested)
                {
                    return nested;
                }
            }
        }
        else if (original.InnerException is { } inner)
        {
            return Find(inner);
        }
        return null;
    }
}
