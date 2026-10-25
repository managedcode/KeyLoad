using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes AuthorizationCommands operations against the caller's database context.</summary>
public static class AuthorizationCommands
{
    internal static void AuthorizeBatchMutations(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, CommandRequest request, bool allowEmpty)
    {
        const int EmptyMutationsLength = 0;
        if (!allowEmpty && request.Mutations.Length == EmptyMutationsLength)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, CommandAuthorization.MutationCountBudgetMessage);
        }

        foreach (var mutation in request.Mutations)
        {
            ProtocolValidation.ValidateMutationStructure(mutation);
            if (allowEmpty && mutation is QueueToGraph or GraphToQueueMutation or AdvanceQueueDeadline)
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandAuthorization.CompositionBatchRequiredMessage);
            }

            JsonData.Identifier(mutation.Resource);
            engine.Authorization.Require(principal, request.Partition, mutation.Resource, BatchMutationCapability(mutation));
            engine.RequireQueueTransferCapabilities(principal, mutation);
            engine.RequireRecurringSagaCapabilities(principal, mutation);
            var resource = engine.BatchResource(view, request.Partition, mutation.Resource);
            if (mutation is StoreAggregateSnapshot)
            {
                engine.Authorization.RequireReplayInput(principal, resource);
            }

            engine.AuthorizeComposition(view, principal, request.Partition, mutation);
            engine.AuthorizeExtendedMutation(view, principal, request.Partition, mutation);
        }
    }

    internal static ResourceDefinition BatchResource(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null) => engine.Resource(view, partition, name, kind);
    internal static Capability BatchMutationCapability(Mutation mutation)
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
            UpsertEdge or DeleteEdge or QueueToGraph or ApplyCrossPartitionReverseEdge or CompleteCrossPartitionReverseEdge => Capability.GraphWrite,
            GraphToQueueMutation => Capability.QueuePublish,
            CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer => Capability.QueuePublish,
            AdvanceQueueTransferAttempt or AdvanceQueueTransferRepair => Capability.QueuePublish | Capability.QueueInspect,
            ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule or CompareExchangeSaga or ExpireSaga => Capability.SchedulerManage | Capability.QueuePublish,
            AppendSamples => Capability.SeriesAppend,
            ExpireSamples => Capability.SeriesManage,
            RefreshSampleRollup or OpenSampleChunkWindow or SealSampleChunkWindow or MergeSampleChunkWindow or DropSampleChunkWindow => Capability.SeriesManage | Capability.SeriesRead,
            DropSampleRollup => Capability.SeriesManage,
            StoreAggregateSnapshot => Capability.EventsSnapshotsManage | Capability.EventsRead,
            PutVector => Capability.DocumentsWrite,
            global::KeyLoad.ApplyVectorProjection => Capability.DocumentsWrite,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandAuthorization.UnsupportedBatchMutationMessage)
        };
    }
}
