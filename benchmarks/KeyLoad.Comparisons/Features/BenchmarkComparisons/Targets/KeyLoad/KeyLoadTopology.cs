namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget
{
    private const int SingleItemCount = 1;
    private const string Rf3Required = "KeyLoadRf3Required";
    private const string BenchmarkTopologyMismatch = "KeyLoadBenchmarkTopologyMismatch";
    private async Task ObserveCopiesAsync(CancellationToken token)
        => Profile = await KeyLoadTopologyObserver.ObserveAsync(peerClients, credential, clientOptions,
            expectedNodeCount, timeProvider, lifecycle, admissionObservations, Profile, token).ConfigureAwait(false);
}
