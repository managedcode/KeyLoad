using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Writes one immutable control record to every configured voter with rollback on partial failure.</summary>
internal static class RequestCqrsProbeBroadcast
{
    internal static void WriteIdentical(IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, string> directories, IReadOnlyDictionary<string, byte[]> owners,
        string fileName, byte[] bytes)
    {
        foreach (var node in nodes)
        {
            var directory = directories[node];
            RequestCqrsProbeFileStore.VerifyOwnerFile(directory, owners[node]);
            RequestCqrsProbeFileStore.EnsureCanWrite(directory, fileName, bytes.Length);
        }
        var written = new List<string>(nodes.Count);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            foreach (var node in nodes)
            {
                var directory = directories[node];
                RequestCqrsProbeFileStore.WriteAtomic(directory, fileName, bytes);
                written.Add(directory);
            }
        }, failures);
        if (failures.Count == 0)
        { return; }
        foreach (var directory in written)
        { ServerFailureObserver.Observe(() => RequestCqrsProbeFileStore.DeleteExactFile(directory, fileName, bytes), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
