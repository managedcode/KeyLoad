using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes SearchQueries operations against the caller's database context.</summary>
public static class SearchQueries
{
    internal static AnnProjectionWork ReadAnnProjectionWork(this DatabaseEngine engine, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget) => engine.Store.Read(view => engine.ReadAnnProjectionWork(view, consumer, generation, budget));
    internal static AnnProjectionWork ReadAnnProjectionWorkBound(this DatabaseEngine engine, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.Store.Read(view => engine.ReadAnnProjectionWork(view, consumer, generation, budget, binding));
    }

    internal static AnnProjectionWorkPage ReadAnnProjectionWorkPage(this DatabaseEngine engine, byte[]? after, ReadExecutionBudget budget) => engine.ReadAnnProjectionWorkPageCore(after, budget, null);
    internal static AnnProjectionWorkPage ReadAnnProjectionWorkPageBound(this DatabaseEngine engine, byte[]? after, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        var page = engine.ReadAnnProjectionWorkPageCore(after, budget, binding);
        return page;
    }

    internal static AnnProjectionWorkPage ReadAnnProjectionWorkPageCore(this DatabaseEngine engine, byte[]? after, ReadExecutionBudget budget, IResourceLifetimeWorkBinding? binding)
    {
        const int LastConsumerRecordOffset = 1;
        return engine.Store.Read<AnnProjectionWorkPage>(view =>
        {
            var bounded = budget.CreateView(view);
            var page = bounded.Scan(KeyCodec.Encode(ProjectionOutbox.ProjectionConsumerSpace), bounded.MaximumRangeRecords, after);
            var items = ImmutableArray.CreateBuilder<AnnProjectionWork>();
            var rejected = ImmutableArray.CreateBuilder<ErrorCode>();
            foreach (var row in page.Records)
            {
                var current = OrleansSerialization.Deserialize<ProjectionConsumerInfo>(row.Value.Span);
                if (!row.Key.Span.SequenceEqual(ProjectionOutbox.ConsumerKey(current.Consumer)))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ProjectionMaintenanceConfiguration.InvalidMaintenanceAuthority);
                }

                if (current.Released || current.Definition.AnnMaintenance is null)
                {
                    continue;
                }

                try
                {
                    items.Add(engine.ReadAnnProjectionWork(binding is null ? bounded : view, current.Consumer, current.Definition.IndexGeneration, budget, binding));
                }
                catch (KeyLoadException refusal) when (refusal.Code is ErrorCode.PermissionDenied or ErrorCode.Unauthenticated or ErrorCode.TokenInvalidated)
                {
                    rejected.Add(refusal.Code);
                }
            }

            return new(items.ToImmutable(), page.HasMore ? page.Records[^LastConsumerRecordOffset].Key.ToArray() : null, rejected.ToImmutable());
        });
    }

    internal static AnnProjectionWork ReadAnnProjectionWork(this DatabaseEngine engine, global::KeyLoad.Storage.IKeyValueView view, ProjectionConsumerRef consumer, long generation, ReadExecutionBudget budget, IResourceLifetimeWorkBinding? binding = null)
    {
        var current = binding is null ? engine.ReadProjectionMaintenanceEligibility(view, consumer, generation, budget) : engine.ReadProjectionMaintenanceEligibilityCore(view, consumer, generation, budget, null, true);
        var bounded = budget.CreateView(view);
        var authority = current.MaintenanceAuthority ?? throw Errors.Fail(ErrorCode.TokenInvalidated, ProjectionMaintenanceConfiguration.InvalidMaintenanceAuthority);
        var principal = engine.Principal(bounded, authority.PrincipalId, engine.EvaluationClock.GetUtcNow());
        var placement = engine.ReadAtomicPartitionPlacement(bounded, principal.Id, consumer.Partition);
        var physical = new PhysicalShardRecord(placement.PhysicalShardId, placement.Incarnation);
        if (physical.Incarnation != engine.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ProjectionMaintenanceConfiguration.InvalidMaintenanceAuthority);
        }

        return new(current, principal, engine.Store.Identity.NodeId, physical, engine.ReadOutboxHead(bounded, consumer.Partition).Tail);
    }
}
