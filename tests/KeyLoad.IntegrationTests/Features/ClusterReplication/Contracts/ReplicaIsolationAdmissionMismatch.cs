namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal enum ReplicaIsolationAdmissionMismatch
{
    None = 0,
    NotRunning = 1,
    Privileged = 2,
    SharedPidNamespace = 3,
    NetworkCount = 4,
    NetworkMode = 5,
    CapabilityCount = 6,
    Capability = 7,
    ContainerName = 8,
    ConfiguredImage = 9,
    RuntimeUser = 10,
    BuildSource = 11,
    Incarnation = 12
}
