namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeAnnRetiredOriginalIdentity.ContractAlias)]
internal sealed record NativeAnnRetiredOriginalIdentity(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] byte[] RequestSha256)
{
    internal const string ContractAlias = "keyload.server.ann-retired-original-identity.v1";
}
