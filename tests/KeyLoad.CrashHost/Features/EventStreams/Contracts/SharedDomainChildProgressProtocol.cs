namespace KeyLoad.CrashHost.Features.EventStreams;

internal static class SharedDomainChildProgressProtocol
{
    internal const string FileName = "kl081-child-progress.bin";
    internal const string Alias = "keyload.test.shared-domain-child-progress.v1";
    internal const string PhaseAlias = "keyload.test.shared-domain-child-progress-phase.v1";
    internal const string Invalid = "The original shared-domain child progress witness is invalid.";
    internal const int Version = 1;
    internal const int MaximumBytes = 1024;
    internal const int InitialOrdinal = 0;
    internal const long EmptyPosition = 0L;
}

[Orleans.Alias(SharedDomainChildProgressProtocol.PhaseAlias)]
internal enum SharedDomainChildProgressPhase
{
    DatabaseBootstrap = 1,
    ResourceConfiguration = 2,
    SeedProducer = 3,
    QueueReceive = 4,
    QueueAcknowledgement = 5,
    SaveOriginalOperation = 6,
    SaveSeedOperations = 7,
    SaveSeedResults = 8,
    SaveSeedImage = 9,
    SaveSeedPosition = 10,
    SaveIdentityDigest = 11,
    ArmingOriginalBoundary = 12
}

[Orleans.GenerateSerializer, Orleans.Alias(SharedDomainChildProgressProtocol.Alias)]
internal sealed record SharedDomainChildProgressV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] SharedDomainChildProgressPhase Phase,
    [property: Orleans.Id(2)] int SeedOrdinal,
    [property: Orleans.Id(3)] double ElapsedMilliseconds,
    [property: Orleans.Id(4)] long StorePosition);
