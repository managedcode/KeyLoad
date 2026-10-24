namespace KeyLoad.RecoveryTests.EventStreams;

[Orleans.GenerateSerializer, Orleans.Alias(SharedDomainChildProgressProtocol.Alias)]
internal sealed record SharedDomainChildProgress(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] SharedDomainChildProgressPhase Phase,
    [property: Orleans.Id(2)] int SeedOrdinal,
    [property: Orleans.Id(3)] double ElapsedMilliseconds,
    [property: Orleans.Id(4)] long StorePosition);
