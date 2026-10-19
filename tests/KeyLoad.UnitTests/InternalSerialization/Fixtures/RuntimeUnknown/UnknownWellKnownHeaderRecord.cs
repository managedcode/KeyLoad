namespace KeyLoad.UnitTests.Features.InternalSerialization;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(UnknownWellKnownHeaderFixture.Alias)]
internal sealed record UnknownWellKnownHeaderRecord(
    [property: global::Orleans.Id(0)] string Text,
    [property: global::Orleans.Id(1)] int Number);
