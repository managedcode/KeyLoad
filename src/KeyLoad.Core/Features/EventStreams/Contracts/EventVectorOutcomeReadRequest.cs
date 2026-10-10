namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorOutcomeReadRequest.SerializerAlias)]
internal sealed record EventVectorOutcomeReadRequest(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] EventVectorSourcePhase? OriginalSourcePhase)
{
    internal const int ControlRequestField = 2;
    internal const int ControlOwnerField = 3;
    [Orleans.Id(ControlRequestField)]
    public EventFeedControlRequest? OriginalControlRequest { get; init; }
    [Orleans.Id(ControlOwnerField)]
    public PhysicalShardRecord? ControlOwner { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-outcome-read-request.v1";
}
