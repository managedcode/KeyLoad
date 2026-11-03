namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(TextOnlineStaging.SerializationAlias)]
internal sealed record TextOnlineStaging(
    [property: global::Orleans.Id(1)] Guid NodeId,
    [property: global::Orleans.Id(2)] string Leaf,
    [property: global::Orleans.Id(3)] OnlineTextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(4)] string PrincipalId,
    [property: global::Orleans.Id(5)] string ParentFingerprint,
    [property: global::Orleans.Id(6)] Guid? ExpectedCurrentCommandId,
    [property: global::Orleans.Id(7)] TextIndexSourceCut CapturedCut)
{
    internal const string SerializationAlias = "keyload.server.execution-text.online-staging";
}
