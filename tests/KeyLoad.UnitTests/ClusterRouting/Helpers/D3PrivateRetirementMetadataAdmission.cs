using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns numeric codec-test admission from the actual fixture operation budget, without claiming remote accounting.</summary>
internal static class D3PrivateRetirementMetadataAdmission
{
    private const int CurrentVersion = 1;
    private const int MetadataShareDivisor = 2;

    internal static async Task RunAsync(ReadExecutionBudget work, DateTimeOffset expiry,
        Func<NativeCommandMetadataAdmissionV1, Task> execute)
    {
        work.ConstrainLifetime(expiry);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes / MetadataShareDivisor,
            work.RemainingReadGrantRecords / MetadataShareDivisor);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => execute(new(CurrentVersion, grant.RemainingBytes,
            grant.RemainingRecords, work.MaximumResultBytes)), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
