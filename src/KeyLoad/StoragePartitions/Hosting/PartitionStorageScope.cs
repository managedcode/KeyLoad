using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Binds scoped services to the exact open files and physical work ledger they borrow.</summary>
internal sealed class PartitionStorageScope
{
    private const string Unbound = "The physical service scope has not been bound to its open files.";
    private PartitionHost? host;
    private RequestWorkOwner? work;

    internal PartitionHost Host => host ?? throw new InvalidOperationException(Unbound);
    internal RequestWorkOwner Work => work ?? throw new InvalidOperationException(Unbound);

    internal void Bind(PartitionHost actualHost, RequestWorkOwner actualWork)
    {
        ArgumentNullException.ThrowIfNull(actualHost);
        ArgumentNullException.ThrowIfNull(actualWork);
        if (host is not null || work is not null)
        {
            throw new InvalidOperationException(Unbound);
        }
        host = actualHost;
        work = actualWork;
    }
}
