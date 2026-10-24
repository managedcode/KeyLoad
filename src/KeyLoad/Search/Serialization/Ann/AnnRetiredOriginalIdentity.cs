namespace KeyLoad.Server.Search;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(AnnRetiredOriginalIdentity.ContractAlias)]
internal sealed record AnnRetiredOriginalIdentity(
    [property: global::Orleans.Id(0)] Guid CommandId,
    [property: global::Orleans.Id(1)] byte[] RequestSha256)
{
    internal const string ContractAlias = "keyload.server.ann-retired-original-identity";
}
