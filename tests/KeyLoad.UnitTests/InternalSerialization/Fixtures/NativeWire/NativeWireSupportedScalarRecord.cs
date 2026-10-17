namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeWireSupportedScalarFixtures.Alias)]
internal sealed record NativeWireSupportedScalarRecord(
    [property: global::Orleans.Id(0)] int? Identifier,
    [property: global::Orleans.Id(1)] NativeWireSupportedScalarKind Kind,
    [property: global::Orleans.Id(2)] string Name,
    [property: global::Orleans.Id(3)] byte[] Payload,
    [property: global::Orleans.Id(4)] List<int> Values);
