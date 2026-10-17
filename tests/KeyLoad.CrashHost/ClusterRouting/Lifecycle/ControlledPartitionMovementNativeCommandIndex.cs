using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Replication;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Indexes only immutable committed fixture entries; native entries and outcomes remain authoritative.</summary>
internal sealed class ControlledPartitionMovementNativeCommandIndex(long maximumEntries)
{
    internal const string MissingEntry = "The original movement fixture replica entry is missing.";
    private const long IndexStep = 1;
    private readonly Dictionary<(Guid Id, string Principal, CommandOutcomePartitionScope Scope), long> committed = [];
    private long indexedThrough;

    internal long? Find(DurableReplicaLog log, ReplicaConfiguration configuration,
        ReplicatedOperation actual, CancellationToken token)
    {
        var state = log.State;
        if (state.LastIndex > maximumEntries || state.CommittedIndex < indexedThrough || state.Snapshot is not null)
        {
            throw new InvalidOperationException(MissingEntry);
        }
        IndexCommitted(log, configuration, state.CommittedIndex, token);
        var key = Key(actual);
        if (committed.TryGetValue(key, out var found))
        {
            token.ThrowIfCancellationRequested();
            var entry = log.ReadEntry(found);
            if (entry?.Operation is not { } original || entry.Index != found || Key(original) != key)
            {
                throw new InvalidOperationException(MissingEntry);
            }
            return found;
        }
        return FindUncommitted(log, configuration, actual, state.CommittedIndex + IndexStep, state.LastIndex, token);
    }

    private void IndexCommitted(DurableReplicaLog log, ReplicaConfiguration configuration, long through,
        CancellationToken token)
    {
        while (indexedThrough < through)
        {
            token.ThrowIfCancellationRequested();
            var page = log.Read(checked(indexedThrough + IndexStep), configuration.MaxAppendEntries,
                configuration.MaxAppendBytes);
            if (page.IsDefaultOrEmpty)
            {
                throw new InvalidOperationException(MissingEntry);
            }
            foreach (var entry in page)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Index > through)
                {
                    break;
                }
                if (entry.Index != checked(indexedThrough + IndexStep))
                {
                    throw new InvalidOperationException(MissingEntry);
                }
                if (entry.Operation is { } operation)
                {
                    committed.TryAdd(Key(operation), entry.Index);
                }
                indexedThrough = entry.Index;
            }
        }
    }

    private static long? FindUncommitted(DurableReplicaLog log, ReplicaConfiguration configuration,
        ReplicatedOperation actual, long first, long last, CancellationToken token)
    {
        var key = Key(actual);
        while (first <= last)
        {
            token.ThrowIfCancellationRequested();
            var page = log.Read(first, configuration.MaxAppendEntries, configuration.MaxAppendBytes);
            if (page.IsDefaultOrEmpty)
            {
                throw new InvalidOperationException(MissingEntry);
            }
            foreach (var entry in page)
            {
                token.ThrowIfCancellationRequested();
                if (entry.Index != first || entry.Index > last)
                {
                    throw new InvalidOperationException(MissingEntry);
                }
                if (entry.Operation is { } previous && Key(previous) == key)
                {
                    return entry.Index;
                }
                first = checked(entry.Index + IndexStep);
            }
        }
        return null;
    }

    private static (Guid Id, string Principal, CommandOutcomePartitionScope Scope) Key(ReplicatedOperation operation)
        => (operation.Id, operation.PrincipalId, CommandOutcomePartitionIdentity.Resolve(operation));
}
