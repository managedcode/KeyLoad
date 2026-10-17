using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

internal static class TestDatabaseRetainedCommandScope
{
    private const string MissingEntry = "The native fixture retained log entry is missing.";
    private const string Ambiguous = "The native fixture command ID has more than one retained scope.";

    internal static long? Resolve(DurableReplicaLog log, ReplicatedOperation original)
    {
        for (var index = 1L; index <= log.State.LastIndex; index++)
        {
            var entry = log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
            if (entry.Operation is { } retained && SameIdentity(retained, original))
            {
                return index;
            }
        }
        return null;
    }

    internal static long? ResolveUnique(DurableReplicaLog log, Guid id)
    {
        for (var index = 1L; index <= log.State.LastIndex; index++)
        {
            var entry = log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
            if (entry.Operation is not { } retained || retained.Id != id)
            {
                continue;
            }
            RequireMatchingScopes(log, index, retained);
            return index;
        }
        return null;
    }

    private static void RequireMatchingScopes(DurableReplicaLog log, long selectedIndex, ReplicatedOperation selected)
    {
        var index = selectedIndex;
        while (index < log.State.LastIndex)
        {
            index++;
            var entry = log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
            if (entry.Operation is { } retained && retained.Id == selected.Id && !SameIdentity(selected, retained))
            {
                throw new InvalidOperationException(Ambiguous);
            }
        }
    }

    private static bool SameIdentity(ReplicatedOperation first, ReplicatedOperation second)
        => first.Id == second.Id && string.Equals(first.PrincipalId, second.PrincipalId, StringComparison.Ordinal)
            && global::KeyLoad.Core.Features.ClusterRouting.Identity.CommandOutcomePartitionIdentity.Resolve(first)
                == global::KeyLoad.Core.Features.ClusterRouting.Identity.CommandOutcomePartitionIdentity.Resolve(second);
}
