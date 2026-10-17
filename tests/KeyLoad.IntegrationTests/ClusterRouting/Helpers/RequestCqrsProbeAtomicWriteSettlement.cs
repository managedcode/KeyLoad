using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Retains failed atomic publication cleanup without replacing its original failure.</summary>
internal static class RequestCqrsProbeAtomicWriteSettlement
{
    internal static void RemoveFailedPublication(string directory, string fileName, byte[] expected,
        List<Exception> cleanupFailures)
    {
        ServerFailureObserver.Observe(
            () => RequestCqrsProbeFileStore.DeleteExactFile(directory, fileName, expected), cleanupFailures);
    }
}
