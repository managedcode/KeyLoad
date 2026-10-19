namespace KeyLoad.Clustering;

/// <summary>Canonical execution worker-pool addressing; Orleans owns individual activation IDs.</summary>
internal static class RequestWorkerIdentity
{
    private const string RequestPool = "requests";
    internal static string ForRequests() => RequestPool;
}
