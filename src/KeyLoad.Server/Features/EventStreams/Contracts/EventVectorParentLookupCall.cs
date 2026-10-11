namespace KeyLoad.Server.Features.EventStreams;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(EventVectorParentLookupCall.SerializerAlias)]
internal sealed record EventVectorParentLookupCall(
    [property: global::Orleans.Id(EventVectorParentLookupCall.Field0)] int Version,
    [property: global::Orleans.Id(EventVectorParentLookupCall.Field1)] string PrincipalId,
    [property: global::Orleans.Id(EventVectorParentLookupCall.Field2)] EventFeedControlRequest OriginalControlRequest)
{
    internal const int TailObservationField = 4;
    [global::Orleans.Id(TailObservationField)]
    public bool TailObservation { get; init; }

    [global::Orleans.Id(Field3)]
    public ReadOnlyMemory<byte> FreshSourceAuthorizationWitness { get; init; }

    internal const int Field3 = 3;
    internal const string SerializerAlias = "keyload.server.event-vector-parent-lookup-call.v1";
    internal const int Field0 = 0;
    internal const int Field1 = 1;
    internal const int Field2 = 2;
}
