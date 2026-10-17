namespace KeyLoad;

internal static class MutationDiscriminatorNames
{
    internal const string Property = "kind";
    internal const string PutDocument = "putDocument";
    internal const string UpsertAnalyticsEvents = AnalyticsStoreProtocol.UpsertKind;
    internal const string DeleteAnalyticsEvents = AnalyticsStoreProtocol.DeleteKind;
    internal const string PatchDocument = "patchDocument";
    internal const string DeleteDocument = "deleteDocument";
    internal const string AppendEvents = "appendEvents";
    internal const string PublishTopic = "publishTopic";
    internal const string PurgeTopic = "purgeTopic";
    internal const string EnqueueMessage = "enqueue";
    internal const string UpsertEdge = "upsertEdge";
    internal const string DeleteEdge = "deleteEdge";
    internal const string ApplyCrossPartitionReverseEdge = "applyCrossPartitionReverseEdge";
    internal const string CompleteCrossPartitionReverseEdge = "completeCrossPartitionReverseEdge";
    internal const string AppendSamples = "appendSamples";
    internal const string ExpireSamples = "expireSamples";
    internal const string RefreshSampleRollup = SampleRollupProtocol.RefreshKind;
    internal const string DropSampleRollup = SampleRollupProtocol.DropKind;
    internal const string StoreAggregateSnapshot = "storeAggregateSnapshot";
    internal const string PutVector = "putVector";
    internal const string QueueToGraph = "queueToGraph";
    internal const string GraphToQueue = "graphToQueue";
    internal const string CreateQueueTransfer = "createQueueTransfer";
    internal const string AcceptQueueTransfer = "acceptQueueTransfer";
    internal const string CompleteQueueTransfer = "completeQueueTransfer";
    internal const string ApplyVectorProjection = "applyVectorProjection";
    internal const string ConfigureRecurringSchedule = "configureRecurringSchedule";
    internal const string EmitRecurringOccurrences = "emitRecurringOccurrences";
    internal const string CancelRecurringSchedule = "cancelRecurringSchedule";
    internal const string CompareExchangeSaga = "compareExchangeSaga";
    internal const string ExpireSaga = "expireSaga";
}

// Only the trusted server constructs this envelope. Caller roles and timestamps never come from public JSON.
