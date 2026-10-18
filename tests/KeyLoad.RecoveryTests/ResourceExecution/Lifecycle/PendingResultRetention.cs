using KeyLoad.Clustering;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

/// <summary>Owns an unattached result ledger until its original lease accepts ownership.</summary>
internal sealed class PendingResultRetention(IOptions<DatabaseLimits> options) : IDisposable
{
    private const string MissingRetention = "The pending result ledger has already been transferred or was not acquired.";
    private ResourceLifetimeWorkResultRetention? pending = new(options);

    internal ResourceLifetimeWorkResultRetention TransferTo(NativeRequestWorkLease lease)
    {
        var retention = pending ?? throw new InvalidOperationException(MissingRetention);
        lease.AttachResultRetention(retention);
        pending = null;
        return retention;
    }

    public void Dispose()
    {
        pending?.Dispose();
        pending = null;
    }
}
