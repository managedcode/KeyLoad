using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server;

/// <summary>Borrowed immutable page; release participates in the original source owner drain.</summary>
internal sealed class PartitionMovementImagePageLease : IDisposable
{
    private PartitionMovementImageSession? owner;
    internal PartitionMovementImagePageLease(PartitionMovementImageSession owner, PartitionMoveImagePage page)
    {
        this.owner = owner;
        Page = page;
    }

    internal PartitionMoveImagePage Page { get; }

    public void Dispose()
    { Interlocked.Exchange(ref owner, null)?.Return(); }
}
