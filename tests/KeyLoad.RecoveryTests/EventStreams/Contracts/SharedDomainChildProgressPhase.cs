namespace KeyLoad.RecoveryTests.EventStreams;

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
