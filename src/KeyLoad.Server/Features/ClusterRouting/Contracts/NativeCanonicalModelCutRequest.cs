namespace KeyLoad.Server.Features.ClusterRouting;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(NativeCanonicalModelCutDiagnosticProtocol.RequestAlias)]
internal sealed record NativeCanonicalModelCutRequest(
    [property: global::Orleans.Id(0)] Guid Nonce,
    [property: global::Orleans.Id(1)] Guid ArmId,
    [property: global::Orleans.Id(2)] string SessionId,
    [property: global::Orleans.Id(3)] PartitionRef SourcePartition,
    [property: global::Orleans.Id(4)] PartitionRef DestinationPartition,
    [property: global::Orleans.Id(5)] Guid AcceptCommandId,
    [property: global::Orleans.Id(6)] string LogicalPrincipalId,
    [property: global::Orleans.Id(7)] string TechnicalPrincipalId,
    [property: global::Orleans.Id(8)] QueueLaneRef SourceQueue,
    [property: global::Orleans.Id(9)] Guid TransferId,
    [property: global::Orleans.Id(10)] QueueLaneRef DestinationQueue);
