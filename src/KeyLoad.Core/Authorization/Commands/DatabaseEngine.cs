using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeBatchMutations(IKeyValueView view, PrincipalRecord principal, CommandRequest request,
        bool allowEmpty)
    {
        const int EmptyMutationsLength = 0;
        if (!allowEmpty && request.Mutations.Length == EmptyMutationsLength || request.Mutations.Length > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage);
        }

        foreach (var mutation in request.Mutations)
        {
            ValidateMutationStructure(mutation);
            if (allowEmpty && mutation is QueueToGraph or GraphToQueueMutation or AdvanceQueueDeadline)
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, CompositionBatchRequiredMessage);
            }
            JsonData.Identifier(mutation.Resource);
            Authorization.Require(principal, request.Partition, mutation.Resource, BatchMutationCapability(mutation));
            RequireQueueTransferCapabilities(principal, mutation);
            RequireRecurringSagaCapabilities(principal, mutation);
            var resource = BatchResource(view, request.Partition, mutation.Resource);
            if (mutation is StoreAggregateSnapshot)
            {
                Authorization.RequireReplayInput(principal, resource);
            }
            AuthorizeComposition(view, principal, request.Partition, mutation);
            AuthorizeExtendedMutation(view, principal, request.Partition, mutation);
        }
    }

    private ResourceDefinition BatchResource(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
        => Resource(view, partition, name, kind);

    private static Capability BatchMutationCapability(Mutation mutation)
    {
        return mutation switch
        {
            UpsertAnalyticsEvents or DeleteAnalyticsEvents => Capability.AnalyticsWrite,
            InsertTypedRow or PutDocument or PatchDocument or DeleteDocument => Capability.DocumentsWrite,
            AppendEvents => Capability.EventsAppend,
            PublishTopic => Capability.TopicsPublish,
            PurgeTopic => Capability.SchemaManage | Capability.TopicsRead,
            EnqueueMessage => Capability.QueuePublish,
            AdvanceQueueDeadline => Capability.QueueConsume,
            RedriveQueueMessage or ParkPendingQueueMessage => Capability.DeadLettersRedrive,
            CancelQueueMessage => Capability.QueueCancel,
            UpsertEdge or DeleteEdge or QueueToGraph or ApplyCrossPartitionReverseEdge
                or CompleteCrossPartitionReverseEdge => Capability.GraphWrite,
            GraphToQueueMutation => Capability.QueuePublish,
            CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer => Capability.QueuePublish,
            AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair => Capability.QueuePublish | Capability.QueueInspect,
            ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule
                or CompareExchangeSaga or ExpireSaga => Capability.SchedulerManage | Capability.QueuePublish,
            AppendSamples => Capability.SeriesAppend,
            ExpireSamples => Capability.SeriesManage,
            RefreshSampleRollup or OpenSampleChunkWindow or SealSampleChunkWindow or MergeSampleChunkWindow
                or DropSampleChunkWindow => Capability.SeriesManage | Capability.SeriesRead,
            DropSampleRollup => Capability.SeriesManage,
            StoreAggregateSnapshot => Capability.EventsSnapshotsManage | Capability.EventsRead,
            PutVector => Capability.DocumentsWrite,
            global::KeyLoad.ApplyVectorProjection => Capability.DocumentsWrite,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedBatchMutationMessage)
        };
    }

}
