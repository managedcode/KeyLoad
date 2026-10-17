namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(NativeUnknownWellKnownHeaderFixture.Alias)]
internal sealed record NativeUnknownWellKnownHeaderRecord(
    [property: global::Orleans.Id(0)] string Text,
    [property: global::Orleans.Id(1)] int Number);
