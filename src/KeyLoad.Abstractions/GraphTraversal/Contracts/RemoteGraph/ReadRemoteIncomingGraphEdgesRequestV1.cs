namespace KeyLoad;

/// <summary>Read remote incoming graph edges request.</summary>
/// <param name="Version">Version.</param>
/// <param name="Target">Target.</param>
/// <param name="Graph">Graph.</param>
/// <param name="Limit">Limit.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ReadRemoteIncomingGraphEdgesRequestV1.Alias)]
public sealed record ReadRemoteIncomingGraphEdgesRequestV1(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] EntityRef Target,
    [property: global::Orleans.Id(2)] string Graph,
    [property: global::Orleans.Id(3)] int Limit)
{
    internal const string Alias = "keyload.contract.remote-graph-incoming-request.v1";
}
