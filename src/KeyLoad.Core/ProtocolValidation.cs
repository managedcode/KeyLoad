namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ValidatePrincipalStructure(PrincipalRecord principal)
    {
        if (principal.Grants.IsDefault || principal.FieldGrants.IsDefault || principal.Projects.IsDefault
            || principal.Grants.Length > 256 || principal.FieldGrants.Length > 256 || principal.Projects.Length > 256
            || principal.Grants.Any(grant => grant is null || (grant.Capabilities & ~Capability.All) != 0))
        {
            throw Errors.Fail(ErrorCode.Validation, "The principal scope exceeds its budget or contains an invalid grant.");
        }

        foreach (var grant in principal.Grants)
        { JsonData.Identifier(grant.Database); JsonData.Identifier(grant.Resource); }
        foreach (var field in principal.FieldGrants)
        {
            JsonData.Identifier(field);
        }

        foreach (var project in principal.Projects)
        {
            JsonData.Identifier(project);
        }

        if (principal.OwnerId is { } owner)
        {
            JsonData.Identifier(owner);
        }
    }
    private static void ValidateMutationStructure(Mutation mutation)
    {
        if (mutation is null)
        {
            throw Errors.Fail(ErrorCode.Validation, MissingMutationEntryMessage);
        }

        ValidateExtendedMutationStructure(mutation);

        if (mutation.Resource != CanonicalMutationOwner(mutation))
        {
            throw Errors.Fail(ErrorCode.Validation, "The mutation resource and canonical owner differ.");
        }

        var invalid = mutation switch
        {
            PatchDocument patch => patch.Patches.IsDefault || patch.Patches.Any(item => item is null || item.Kind is not (PatchKind.Set or PatchKind.Remove)),
            AppendEvents append => append.Events.IsDefault || append.Events.Any(item => item is null)
                || append.ExpectedRevision.State is not (ExpectedStreamState.Any or ExpectedStreamState.Exact or ExpectedStreamState.NoStream),
            PublishTopic topic => topic.Events.IsDefault || topic.Events.Any(item => item is null),
            AppendSamples samples => samples.Samples.IsDefault || samples.Samples.Any(item => item is null),
            PutVector vector => vector.Values.IsDefault,
            ApplyVectorProjection projection => projection.Target.Values.IsDefault,
            _ => false
        };
        if (invalid)
        {
            throw Errors.Fail(ErrorCode.Validation, "A nested mutation entry or enum value is invalid.");
        }
    }

    private static string CanonicalMutationOwner(Mutation mutation)
        => mutation switch
        {
            PutDocument put => put.Collection,
            PatchDocument patch => patch.Collection,
            DeleteDocument delete => delete.Collection,
            AppendEvents append => append.StreamSet,
            PublishTopic topic => topic.Topic,
            EnqueueMessage message => message.Queue,
            UpsertEdge edge => edge.Graph,
            DeleteEdge edge => edge.Graph,
            ApplyCrossPartitionReverseEdge edge => edge.Graph,
            CompleteCrossPartitionReverseEdge edge => edge.Graph,
            AppendSamples samples => samples.SeriesSet,
            ExpireSamples samples => samples.SeriesSet,
            StoreAggregateSnapshot snapshot => snapshot.StreamSet,
            PutVector vector => vector.Collection,
            QueueToGraph projection => projection.Graph,
            GraphToQueueMutation projection => projection.Queue,
            CreateQueueTransfer transfer => transfer.SourceQueue.Queue,
            AcceptQueueTransfer transfer => transfer.DestinationQueue.Queue,
            CompleteQueueTransfer transfer => transfer.SourceQueue.Queue,
            ApplyVectorProjection projection => projection.Target.Collection,
            ConfigureRecurringSchedule schedule => schedule.Definition.Lane.Queue,
            EmitRecurringOccurrences schedule => schedule.Lane.Queue,
            CancelRecurringSchedule schedule => schedule.Lane.Queue,
            CompareExchangeSaga saga => saga.Lane.Queue,
            ExpireSaga saga => saga.Lane.Queue,
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, "The mutation is unsupported.")
        };

    private static void ValidateExtendedMutationStructure(Mutation mutation)
    {
        var invalid = mutation switch
        {
            CreateQueueTransfer transfer => transfer.SourceQueue?.Partition is null || transfer.Destination?.Partition is null || transfer.Message is null,
            AcceptQueueTransfer transfer => transfer.DestinationQueue?.Partition is null,
            CompleteQueueTransfer transfer => transfer.SourceQueue?.Partition is null,
            ApplyVectorProjection projection => projection.SourceStream is null || projection.SourceDocument is null
                || projection.SourceStream.Partition is null || projection.SourceDocument.Partition is null
                || projection.Target is null || projection.Target.Space is null,
            ApplyCrossPartitionReverseEdge edge => edge.SourcePartition is null || edge.Destination?.Partition is null,
            CompleteCrossPartitionReverseEdge edge => edge.SourcePartition is null || edge.Destination?.Partition is null,
            ConfigureRecurringSchedule schedule => schedule.Definition?.Lane?.Partition is null,
            EmitRecurringOccurrences schedule => schedule.Lane?.Partition is null,
            CancelRecurringSchedule schedule => schedule.Lane?.Partition is null,
            CompareExchangeSaga saga => saga.Lane?.Partition is null
                || saga.Timeout is { } timeout && timeout.Queue?.Partition is null,
            ExpireSaga saga => saga.Lane?.Partition is null,
            _ => false
        };
        if (invalid)
        {
            throw Errors.Fail(ErrorCode.Validation, MissingMutationEntryMessage);
        }
    }
}
