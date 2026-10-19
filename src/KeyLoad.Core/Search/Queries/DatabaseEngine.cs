using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal AnnProjectionWork ReadAnnProjectionWork(ProjectionConsumerRef consumer, long generation,
        ReadExecutionBudget budget)
        => Store.Read(view => ReadAnnProjectionWork(view, consumer, generation, budget));

    internal AnnProjectionWork ReadAnnProjectionWorkBound(ProjectionConsumerRef consumer, long generation,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return Store.Read(view => ReadAnnProjectionWork(view, consumer, generation, budget, binding));
    }

    internal AnnProjectionWorkPage ReadAnnProjectionWorkPage(byte[]? after, ReadExecutionBudget budget)
        => ReadAnnProjectionWorkPageCore(after, budget, null);

    internal AnnProjectionWorkPage ReadAnnProjectionWorkPageBound(byte[]? after, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        var page = ReadAnnProjectionWorkPageCore(after, budget, binding);
        budget.CheckResult(page);
        return page;
    }

    private AnnProjectionWorkPage ReadAnnProjectionWorkPageCore(byte[]? after, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding? binding)
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
                var current = OrleansSerialization.Deserialize<ProjectionConsumerInfo>(row.Value.Span);
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
                    items.Add(ReadAnnProjectionWork(binding is null ? bounded : view, current.Consumer,
                        current.Definition.IndexGeneration, budget, binding));
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
        ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding? binding = null)
    {
        var current = binding is null ? ReadProjectionMaintenanceEligibility(view, consumer, generation, budget)
            : ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, null, true);
        var bounded = budget.CreateView(view);
        var authority = current.MaintenanceAuthority
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceAuthority);
        var principal = Principal(bounded, authority.PrincipalId, EvaluationClock.GetUtcNow());
        var placement = ReadAtomicPartitionPlacement(bounded, principal.Id, consumer.Partition);
        var physical = new PhysicalShardRecord(placement.PhysicalShardId, placement.Incarnation);
        if (physical.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidMaintenanceAuthority);
        }
        return new(current, principal, Store.Identity.NodeId, physical, ReadOutboxHead(bounded, consumer.Partition).Tail);
    }
}
