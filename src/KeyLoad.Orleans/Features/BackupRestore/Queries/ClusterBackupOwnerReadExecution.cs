using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class ClusterBackupOwnerReadExecution
{
    internal static async Task<ClusterBackupOwnerReceipt> ExecuteAsync(INodeAdministration owner,
        PrincipalRecord principal, ReadOnlyMemory<byte> capability, IOptions<DatabaseLimits> limits,
        TimeProvider clock, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainLifetime(originalExpiry);
        var result = await owner.CaptureClusterBackupOwnerAsync(principal.Id, capability, work,
            cancellationToken).ConfigureAwait(true);
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
