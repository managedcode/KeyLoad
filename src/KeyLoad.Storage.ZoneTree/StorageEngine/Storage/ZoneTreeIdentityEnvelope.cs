namespace KeyLoad.Storage.ZoneTree;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.IdentityEnvelope)]
internal sealed record ZoneTreeIdentityEnvelope(
    [property: global::Orleans.Id(0)] byte[] Payload,
    [property: global::Orleans.Id(1)] byte[] Checksum);
