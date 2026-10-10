namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeTextOnlineStaging.SerializationAlias)]
internal sealed record NativeTextOnlineStaging(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] Guid NodeId,
    [property: global::Orleans.Id(2)] string Leaf,
    [property: global::Orleans.Id(3)] OnlineTextIndexMaintenanceRequest Request,
    [property: global::Orleans.Id(4)] string PrincipalId,
    [property: global::Orleans.Id(5)] string ParentFingerprint,
    [property: global::Orleans.Id(6)] Guid? ExpectedCurrentCommandId,
    [property: global::Orleans.Id(7)] TextIndexSourceCut CapturedCut,
    [property: global::Orleans.Id(8)] int DataEpoch)
{
    internal const string SerializationAlias = "keyload.server.native-text.online-staging.v1";
}
