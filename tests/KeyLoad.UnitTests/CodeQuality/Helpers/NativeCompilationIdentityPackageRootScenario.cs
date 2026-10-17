using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Assertions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal static class NativeCompilationIdentityPackageRootScenario
{
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        NativeCompilationIdentityPackageRootFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new NativeCompilationIdentityPackageRootFixture(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(fixture, failures, cancellationToken), failures);
            await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures);
        }
        NativeCoverageImageNodeSettlement.ThrowFailures(failures);
    }

    private static async Task RunOwnedAsync(NativeCompilationIdentityPackageRootFixture fixture,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        await ServerFailureObserver.ObserveAsync(() => fixture.InitializeAsync(cancellationToken), failures);
        if (failures.Count != 0)
        {
            return;
        }
        var plainRoot = await NativeCompilationIdentityPackageRootBuilder.BuildAsync(fixture, false, false, false, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.SuccessfulProcessAsync(plainRoot);
        var plainMetadata = await NativeCompilationIdentityPackageRootAssertions.ReadMetadataAsync(fixture, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.AssertNativeImageAsync(fixture, cancellationToken);
        var slashRoot = await NativeCompilationIdentityPackageRootBuilder.BuildAsync(fixture, true, false, false, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.SuccessfulProcessAsync(slashRoot);
        var slashMetadata = await NativeCompilationIdentityPackageRootAssertions.ReadMetadataAsync(fixture, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.AssertNativeImageAsync(fixture, cancellationToken);
        await Assert.That(slashMetadata).IsEquivalentTo(plainMetadata, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var original = await NativeCompilationIdentityPackageRootAssertions.CaptureImagesAsync(fixture, cancellationToken);
        await fixture.WriteCounterfeitAsync(cancellationToken);
        var rejected = await NativeCompilationIdentityPackageRootBuilder.BuildAsync(fixture, false, true, false, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.CounterfeitRejectedAsync(rejected);
        await NativeCompilationIdentityPackageRootAssertions.AssertImagesUnchangedAsync(fixture, original, cancellationToken);
        var healthy = await NativeCompilationIdentityPackageRootBuilder.BuildAsync(fixture, true, false, true, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.SuccessfulProcessAsync(healthy);
        await NativeCompilationIdentityPackageRootAssertions.ReadMetadataAsync(fixture, cancellationToken);
        await NativeCompilationIdentityPackageRootAssertions.AssertNativeImageAsync(fixture, cancellationToken);
    }
}
