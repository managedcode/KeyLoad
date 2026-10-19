namespace KeyLoad.Server.Features.Search;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TextProtocol.EnvelopeAlias)]
internal sealed record TextEnvelope(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] byte[] Payload,
    [property: global::Orleans.Id(2)] byte[] Sha256);
