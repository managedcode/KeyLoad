using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void AuthorizeBatchMutations(IKeyValueView view, PrincipalRecord principal, CommandRequest request,
        bool allowEmpty, BatchResourceAdmission admission)
    {
        const int EmptyMutationsLength = 0;
        if (!allowEmpty && request.Mutations.Length == EmptyMutationsLength || request.Mutations.Length > Limits.MaxBatchMutations)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage);
        }

        foreach (var mutation in request.Mutations)
        {
            ValidateMutationStructure(mutation);
            if (allowEmpty && mutation is QueueToGraph or GraphToQueueMutation)
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, CompositionBatchRequiredMessage);
            }
            JsonData.Identifier(mutation.Resource);
            Authorization.Require(principal, request.Partition, mutation.Resource, BatchMutationCapability(mutation));
            var resource = BatchResource(view, request.Partition, mutation.Resource, admission);
            if (mutation is StoreAggregateSnapshot)
            {
                Authorization.RequireReplayInput(principal, resource);
            }
            AuthorizeComposition(view, principal, request.Partition, mutation, admission);
            AuthorizeExtendedMutation(view, principal, request.Partition, mutation);
        }
    }

    private enum BatchResourceAdmission { CurrentPhysicalOwner, RetiredOutcomeMetadata }

    private ResourceDefinition BatchResource(IKeyValueView view, PartitionRef partition, string name, BatchResourceAdmission admission, ResourceKind? kind = null)
    {
        if (admission == BatchResourceAdmission.CurrentPhysicalOwner)
        { return Resource(view, partition, name, kind); }
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name))
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        { throw Errors.Fail(ErrorCode.Conflict, DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail); }
        if (kind is { } required && resource.Kind != required)
        { throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail); }
        return resource;
    }

    private static Capability BatchMutationCapability(Mutation mutation)
    {
        return mutation switch
        {
            PutDocument or PatchDocument or DeleteDocument => Capability.DocumentsWrite,
            AppendEvents => Capability.EventsAppend,
            PublishTopic => Capability.TopicsPublish,
            PurgeTopic => Capability.SchemaManage | Capability.TopicsRead,
            EnqueueMessage => Capability.QueuePublish,
            UpsertEdge or DeleteEdge or QueueToGraph or ApplyCrossPartitionReverseEdge
                or CompleteCrossPartitionReverseEdge => Capability.GraphWrite,
            GraphToQueueMutation => Capability.QueuePublish,
            CreateQueueTransfer or AcceptQueueTransfer or CompleteQueueTransfer => Capability.QueuePublish,
            ConfigureRecurringSchedule or EmitRecurringOccurrences or CancelRecurringSchedule
                or CompareExchangeSaga or ExpireSaga => Capability.SchedulerManage | Capability.QueuePublish,
            AppendSamples => Capability.SeriesAppend,
            ExpireSamples => Capability.SeriesManage,
            RefreshSampleRollup => Capability.SeriesManage | Capability.SeriesRead,
            DropSampleRollup => Capability.SeriesManage,
            StoreAggregateSnapshot => Capability.EventsSnapshotsManage | Capability.EventsRead,
            PutVector => Capability.DocumentsWrite,
            global::KeyLoad.ApplyVectorProjection => Capability.DocumentsWrite,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedBatchMutationMessage)
        };
    }

    private void RequireBatchMutationCapabilities(PrincipalRecord principal, CommandRequest command)
    {
        if (command.Mutations.IsDefaultOrEmpty || command.Mutations.Length > Limits.MaxBatchMutations)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, MutationCountBudgetMessage); }
        foreach (var mutation in command.Mutations)
        {
            Authorization.Require(principal, command.Partition, mutation.Resource, BatchMutationCapability(mutation));
            if (mutation is QueueToGraph forward)
            { ValidateForward(forward); }
            else if (mutation is GraphToQueueMutation reverse)
            { ValidateReverse(command.Partition, reverse); }
            RequireCompositionCapabilities(principal, command.Partition, mutation);
        }
    }

}
