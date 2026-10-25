using KeyLoad.FunctionalTests.CodeQuality.Assertions;
using KeyLoad.FunctionalTests.CodeQuality.Processes;
using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.CodeQuality.Helpers;

internal static class CompilationIdentityPackageRootScenario
{
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        CompilationIdentityPackageRootFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new CompilationIdentityPackageRootFixture(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(fixture, failures, cancellationToken), failures);
            await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(CompilationIdentityPackageRootFixture fixture,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        await ServerFailureObserver.ObserveAsync(() => fixture.InitializeAsync(cancellationToken), failures);
        if (failures.Count != 0)
        {
            return;
        }
        var plainRoot = await CompilationIdentityPackageRootBuilder.BuildAsync(fixture, false, false, false, cancellationToken);
        await CompilationIdentityPackageRootAssertions.SuccessfulProcessAsync(plainRoot);
        var plainMetadata = await CompilationIdentityPackageRootAssertions.ReadMetadataAsync(fixture, cancellationToken);
        await CompilationIdentityPackageRootAssertions.RuntimeAssertImageAsync(fixture, cancellationToken);
        var slashRoot = await CompilationIdentityPackageRootBuilder.BuildAsync(fixture, true, false, false, cancellationToken);
        await CompilationIdentityPackageRootAssertions.SuccessfulProcessAsync(slashRoot);
        var slashMetadata = await CompilationIdentityPackageRootAssertions.ReadMetadataAsync(fixture, cancellationToken);
        await CompilationIdentityPackageRootAssertions.RuntimeAssertImageAsync(fixture, cancellationToken);
        await Assert.That(slashMetadata).IsEquivalentTo(plainMetadata, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var original = await CompilationIdentityPackageRootAssertions.CaptureImagesAsync(fixture, cancellationToken);
        await fixture.WriteCounterfeitAsync(cancellationToken);
        var rejected = await CompilationIdentityPackageRootBuilder.BuildAsync(fixture, false, true, false, cancellationToken);
        await CompilationIdentityPackageRootAssertions.CounterfeitRejectedAsync(rejected);
        await CompilationIdentityPackageRootAssertions.AssertImagesUnchangedAsync(fixture, original, cancellationToken);
        var healthy = await CompilationIdentityPackageRootBuilder.BuildAsync(fixture, true, false, true, cancellationToken);
        await CompilationIdentityPackageRootAssertions.SuccessfulProcessAsync(healthy);
        await CompilationIdentityPackageRootAssertions.ReadMetadataAsync(fixture, cancellationToken);
        await CompilationIdentityPackageRootAssertions.RuntimeAssertImageAsync(fixture, cancellationToken);
    }
}
