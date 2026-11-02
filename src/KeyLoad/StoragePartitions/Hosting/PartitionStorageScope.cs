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
        ArgumentNullException.ThrowIfNull(actualHost) /* KLD0041: BCL null validation accepts the actualHost PartitionHost reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(actualWork) /* KLD0041: BCL null validation accepts the actualWork RequestWorkOwner reference through its object parameter. */;
        if (host is not null || work is not null)
        {
            throw new InvalidOperationException(Unbound);
        }
        host = actualHost;
        work = actualWork;
    }
}
