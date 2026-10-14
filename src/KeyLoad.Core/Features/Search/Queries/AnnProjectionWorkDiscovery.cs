using System.Collections.Immutable;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal AnnProjectionWork ReadAnnProjectionWork(ProjectionConsumerRef consumer, long generation,
        ReadExecutionBudget budget)
        => Store.Read(view => ReadAnnProjectionWork(view, consumer, generation, budget));

    internal AnnProjectionWorkPage ReadAnnProjectionWorkPage(byte[]? after, ReadExecutionBudget budget)
    {
        const int LastConsumerRecordOffset = 1;
        return Store.Read<AnnProjectionWorkPage>(view =>
        {
            var bounded = budget.CreateView(view);
            var page = bounded.Scan(KeyCodec.Encode(ProjectionConsumerSpace), Limits.MaxScanRecords, after);
            var items = ImmutableArray.CreateBuilder<AnnProjectionWork>();
            var rejected = ImmutableArray.CreateBuilder<ErrorCode>();
            foreach (var row in page.Records)
            {
                var current = NativeSerialization.Deserialize<ProjectionConsumerInfo>(row.Value.Span);
                if (!row.Key.Span.SequenceEqual(ConsumerKey(current.Consumer)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, InvalidMaintenanceAuthority);
                }
                if (current.Released || current.Definition.AnnMaintenance is null)
                {
                    continue;
                }
                try
                {
                    items.Add(ReadAnnProjectionWork(bounded, current.Consumer,
                        current.Definition.IndexGeneration, budget));
                }
                catch (KeyLoadException refusal) when (refusal.Code is ErrorCode.PermissionDenied
                    or ErrorCode.Unauthenticated or ErrorCode.TokenInvalidated)
                {
                    rejected.Add(refusal.Code);
                }
            }
            return new(items.ToImmutable(), page.HasMore ? page.Records[^LastConsumerRecordOffset].Key.ToArray() : null,
                rejected.ToImmutable());
        });
    }

    private AnnProjectionWork ReadAnnProjectionWork(global::KeyLoad.Storage.IKeyValueView view,
        ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget)
    {
        var current = ReadProjectionMaintenanceEligibility(view, consumer, generation, budget);
        var bounded = budget.CreateView(view);
        var authority = current.MaintenanceAuthority
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceAuthority);
        var principal = Principal(bounded, authority.PrincipalId, EvaluationClock.GetUtcNow());
        var placement = ReadAtomicPartitionPlacement(bounded, principal.Id,
            new(global::KeyLoad.Core.Features.ClusterRouting.Contracts.AtomicPartitionPlacementProtocol.CurrentVersion,
                consumer.Partition));
        var physical = new PhysicalShardRecord(placement.PhysicalShardId, placement.Incarnation,
            placement.VoterIds, placement.PlacementEpoch);
        if (physical.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidMaintenanceAuthority);
        }
        return new(current, principal, Store.Identity.NodeId, physical, ReadOutboxHead(bounded, consumer.Partition).Tail);
    }
}
