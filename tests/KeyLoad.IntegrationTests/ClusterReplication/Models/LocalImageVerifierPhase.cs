namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal enum LocalImageVerifierPhase
{
    Unobserved,
    DockerContext,
    DockerEndpoint,
    DockerOs,
    Receipt,
    BuildInputs,
    ImageInspect,
    Identity,
    Complete
}
