using KeyLoad.Clustering;

namespace KeyLoad.Server;

/// <summary>Binds scoped services to the exact open files and physical work ledger they borrow.</summary>
internal sealed class PartitionStorageScope
{
    private const string Unbound = "The physical service scope has not been bound to its open files.";
    private PartitionHost? host;
    private NativeRequestWorkOwner? work;

    internal PartitionHost Host => host ?? throw new InvalidOperationException(Unbound);
    internal NativeRequestWorkOwner Work => work ?? throw new InvalidOperationException(Unbound);

    internal void Bind(PartitionHost actualHost, NativeRequestWorkOwner actualWork)
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
