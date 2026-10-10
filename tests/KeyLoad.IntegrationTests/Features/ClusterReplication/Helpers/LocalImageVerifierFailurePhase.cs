using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class LocalImageVerifierFailurePhase
{
    internal const string Marker = "KeyLoadLocalImageVerifierPhase=";

    internal static async Task RecordAsync(Task exit, Task output, Task<string> error, Action<LocalImageVerifierPhase>? observe,
        List<Exception> failures)
    {
        if (observe is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var phase = exit.IsCompletedSuccessfully && output.IsCompletedSuccessfully && error.IsCompletedSuccessfully
                ? Parse(await error.ConfigureAwait(false)) : LocalImageVerifierPhase.Unobserved;
            observe(phase);
        }, failures).ConfigureAwait(false);
    }

    private static LocalImageVerifierPhase Parse(string output)
    {
        var markers = output.Split('\n').Where(line => line.StartsWith(Marker, StringComparison.Ordinal)).ToArray();
        if (markers.Length != 1)
        { return LocalImageVerifierPhase.Unobserved; }
        return markers[0][Marker.Length..].TrimEnd('\r') switch
        {
            nameof(LocalImageVerifierPhase.DockerContext) => LocalImageVerifierPhase.DockerContext,
            nameof(LocalImageVerifierPhase.DockerEndpoint) => LocalImageVerifierPhase.DockerEndpoint,
            nameof(LocalImageVerifierPhase.DockerOs) => LocalImageVerifierPhase.DockerOs,
            nameof(LocalImageVerifierPhase.Receipt) => LocalImageVerifierPhase.Receipt,
            nameof(LocalImageVerifierPhase.BuildInputs) => LocalImageVerifierPhase.BuildInputs,
            nameof(LocalImageVerifierPhase.ImageInspect) => LocalImageVerifierPhase.ImageInspect,
            nameof(LocalImageVerifierPhase.Identity) => LocalImageVerifierPhase.Identity,
            nameof(LocalImageVerifierPhase.Complete) => LocalImageVerifierPhase.Complete,
            _ => LocalImageVerifierPhase.Unobserved
        };
    }
}
