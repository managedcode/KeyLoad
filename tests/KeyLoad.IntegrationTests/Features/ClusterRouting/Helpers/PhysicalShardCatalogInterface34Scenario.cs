using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogInterface34Scenario
{
    private const string RetainedRootKey = "KeyLoad.PhysicalShardCatalog.Interface34Root";

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var roots = PhysicalShardCatalogInterface34Roots.Create();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(roots, cancellationToken), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(roots.Root, recursive: true), failures); }
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            { failure.Data[RetainedRootKey] = roots.Root; }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task ExecuteAsync(PhysicalShardCatalogInterface34Roots roots,
        CancellationToken cancellationToken)
    {
        var images = await PhysicalShardCatalogInterface34Images.ReadAsync(cancellationToken).ConfigureAwait(false);
        var baseline = await PhysicalShardCatalogInterface34BaselineRunner.RunAsync(roots, images, cancellationToken)
            .ConfigureAwait(false);
        var rollbackSource = await PhysicalShardCatalogInterface34Roots.CaptureAsync(roots.RollbackSource, cancellationToken).ConfigureAwait(false);
        await PhysicalShardCatalogInterface34UpgradeRunner.RunAsync(roots, images, baseline, cancellationToken)
            .ConfigureAwait(false);
        await PhysicalShardCatalogInterface34MixedRunner.RunAsync(roots, images, baseline, cancellationToken)
            .ConfigureAwait(false);
        await PhysicalShardCatalogInterface34RollbackRunner.RunAsync(roots, images, baseline, cancellationToken)
            .ConfigureAwait(false);
        await roots.AssertImmutableSourcesAsync(baseline, rollbackSource, cancellationToken).ConfigureAwait(false);
    }
}

internal static class PhysicalShardCatalogInterface34BaselineRunner
{
    internal static async Task<PhysicalShardCatalogInterface34Baseline> RunAsync(
        PhysicalShardCatalogInterface34Roots roots, PhysicalShardCatalogInterface34Images images,
        CancellationToken cancellationToken)
    {
        NodeEpochRf3Profile? profile = null;
        byte[]? profileBytes = null;
        RequestCqrsRf3Workload? workload = null;
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(roots.Baseline, images.AllInterface3(), true, true,
            async wave =>
            {
                var saved = await NodeEpochRf3Profile.ReadAsync(
                    Path.Combine(roots.Baseline, NodeEpochRf3Protocol.ProfileFile), cancellationToken)
                    .ConfigureAwait(false);
                profile = saved.Profile;
                profileBytes = saved.Bytes;
                await PhysicalShardCatalogInterface34Assertions.AssertReadyOnAllNodesAsync(wave.App,
                    cancellationToken).ConfigureAwait(false);
                workload = await RequestCqrsRf3Workload.SeedAsync(wave.App, profile, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        var baseline = new PhysicalShardCatalogInterface34Baseline(
            profile ?? throw new InvalidOperationException("The interface3 AppHost profile was not captured."),
            profileBytes ?? throw new InvalidOperationException("The interface3 profile bytes were not captured."),
            workload ?? throw new InvalidOperationException("The interface3 SDK/MCP workload was not seeded."), []);
        var inventories = await PhysicalShardCatalogInterface34Roots.CaptureAsync(roots.Baseline, cancellationToken).ConfigureAwait(false);
        baseline = baseline with { NodeInventories = inventories };
        await roots.CreatePreUpgradeCopiesAsync(baseline, cancellationToken).ConfigureAwait(false);
        return baseline;
    }
}

internal static class PhysicalShardCatalogInterface34UpgradeRunner
{
    internal static async Task RunAsync(PhysicalShardCatalogInterface34Roots roots,
        PhysicalShardCatalogInterface34Images images, PhysicalShardCatalogInterface34Baseline baseline,
        CancellationToken cancellationToken)
    {
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(roots.Upgrade, images.AllInterface4(), true, true,
            async wave =>
            {
                await PhysicalShardCatalogInterface34Assertions.AssertReadyOnAllNodesAsync(wave.App,
                    cancellationToken).ConfigureAwait(false);
                await baseline.Workload.VerifyPreservedAsync(wave.App, baseline.Profile, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        await PhysicalShardCatalogStoppedOracle.VerifyAsync(roots.Upgrade, baseline.Profile, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal static class PhysicalShardCatalogInterface34MixedRunner
{
    private const string RetainedProbeRootKey = "KeyLoad.PhysicalShardCatalog.Interface34ProbeRoot";
    internal static async Task RunAsync(PhysicalShardCatalogInterface34Roots roots,
        PhysicalShardCatalogInterface34Images images, PhysicalShardCatalogInterface34Baseline baseline,
        CancellationToken cancellationToken)
    {
        if (string.Equals(images.Interface3, images.Interface4, StringComparison.Ordinal))
        { throw new InvalidOperationException("The prior and current verified interface references must differ."); }
        var controls = RequestCqrsProbeFixture.Create(roots.Mixed, Guid.NewGuid(), captureDiscovery: true);
        try
        {
            EntityRef? deniedReference = null;
            await RequestCqrsRf3Epoch7WaveRunner.RunAsync(roots.Mixed, images.Mixed(), true, false,
                async wave =>
                {
                    deniedReference = await PhysicalShardCatalogInterface34MixedAssertions.VerifyAsync(
                        wave.App, baseline.Profile, baseline.Workload, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken, controls).ConfigureAwait(false);
            await PhysicalShardCatalogInterface34ObservationReader.VerifyAsync(controls, cancellationToken)
                .ConfigureAwait(false);
            await PhysicalShardCatalogInterface34DeniedWriteOracle.VerifyAbsentAsync(roots.Mixed, baseline.Profile,
                deniedReference ?? throw new InvalidOperationException("The mixed denied write was not attempted."),
                cancellationToken).ConfigureAwait(false);
            await controls.DisposeAfterResourcesJoinedAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            error.Data[RetainedProbeRootKey] = controls.Root;
            throw;
        }
    }
}

internal static class PhysicalShardCatalogInterface34RollbackRunner
{
    internal static async Task RunAsync(PhysicalShardCatalogInterface34Roots roots,
        PhysicalShardCatalogInterface34Images images, PhysicalShardCatalogInterface34Baseline baseline,
        CancellationToken cancellationToken)
    {
        await roots.RestoreRollbackCopyAsync(baseline, cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(roots.RollbackRun, images.AllInterface3(), true, true,
            async wave =>
            {
                await PhysicalShardCatalogInterface34Assertions.AssertReadyOnAllNodesAsync(wave.App,
                    cancellationToken).ConfigureAwait(false);
                await baseline.Workload.VerifyPreservedAsync(wave.App, baseline.Profile, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
    }
}
