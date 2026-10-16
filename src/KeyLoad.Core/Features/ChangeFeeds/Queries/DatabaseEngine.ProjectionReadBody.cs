using System.Collections.Immutable;
using KeyLoad.Core.Features.ChangeFeeds;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ProjectionBatch ReadProjectionBatchRead(IKeyValueView view, string principalId, ReadProjectionBatchRequest request)
    {
        var now = Clock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        RequireProjectionAdministrator(principal);
        if (request.Limit < MinimumProjectionReadCount || request.Limit > Limits.MaxResults || request.MaxBytes < MinimumProjectionReadCount || request.MaxBytes > Limits.MaxProjectionBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectionBatchBudgetDetail);
        }

        var state = ProjectionConsumer(view, request.Consumer);
        if (state.Released)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReleasedProjectionGenerationDetail);
        }

        BindProjectionReadResources(view, request.Consumer.Partition, state.Definition.Resources);
        var head = ReadOutboxHead(view, request.Consumer.Partition);
        CheckOutboxPosition(head, state.Checkpoint);
        var upper = ProjectionUpperSequence(request, state, head);
        var entries = new List<OutboxEntry>();
        var through = state.Checkpoint;
        var bytes = InitialProjectionPageBytes;
        foreach (var stored in StoredOutboxReader.ReadRange(view, request.Consumer.Partition, through, upper, request.Limit))
        {
            var entry = stored.Entry;
            if (!ProjectionAccepts(state.Definition, entry))
            {
                through = entry.Sequence;
                continue;
            }

            var size = stored.StoredBytes;
            if (size > request.MaxBytes - bytes)
            {
                if (through == state.Checkpoint)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, FirstProjectionEntryExceedsBudgetDetail);
                }

                break;
            }
            entries.Add(entry);
            bytes += size;
            through = entry.Sequence;
        }
        return new(state, entries.ToImmutableArray(), through, Sign(new ProjectionBatchClaims(ProjectionBatchTokenPurpose, Store.Identity.Incarnation,
            request.Consumer, state.Definition.IndexGeneration, state.Checkpoint, through, now.Add(changeFeedExecution.ProjectionBatchLifetime))), through < upper);
    }

    private OutboxStatus GetOutboxStatusRead(IKeyValueView view, string principalId, PartitionRef partition)
    {
        RequireProjectionAdministrator(Principal(view, principalId, Clock.GetUtcNow()));
        ValidatePartition(partition);
        var head = ReadOutboxHead(view, partition);
        var consumers = ProjectionConsumers(view, partition);
        foreach (var consumer in consumers)
        {
            BindProjectionReadResources(view, partition, consumer.Definition.Resources);
            RequireProjectionRetentionCheckpoint(consumer, head);
        }
        return new(head, consumers);
    }
}
