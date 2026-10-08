using KeyLoad.IntegrationTests.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Rejects mixed topology/image/collector admission before creating any native fault infrastructure.</summary>
internal static class ReplicaIsolationAdmission
{
    internal static bool Require(ReplicaIsolationProfile profile)
    {
        if (profile != ReplicaIsolationProfile.OwnedLinuxNamespace)
        { throw new ArgumentOutOfRangeException(nameof(profile)); }
        return true;
    }

    internal static async Task PreflightAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        { throw new PlatformNotSupportedException("The selected namespace fault cohort requires Linux."); }
        if (LocalRf3ImageSelection.ReadNativeArgumentsIfSelected() is not null || LocalRf3ImageSelection.Read() is not null
            || NativeCoverageRf3FixtureOptions.Read() is not null)
        { throw new InvalidOperationException("The namespace fault cohort cannot mix local-image or collector admission."); }
        _ = await ClusterFixtureImageIdentity.ReadVerifiedImageAsync(cancellationToken).ConfigureAwait(false);
    }
}
