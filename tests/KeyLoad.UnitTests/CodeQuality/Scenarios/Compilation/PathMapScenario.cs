using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Assertions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal static class PathMapScenario
{
    private const string SourceDrift = "Compiled source bytes do not match the current source inventory.";
    private const string EscapingPath = "A coverage path is unsafe or escapes the repository.";

    internal static Task CanonicalTamperAndFollowupAsync(CancellationToken cancellationToken)
        => RunOwnedAsync(PathMapFixture.CanonicalMap, VerifyTamperAndFollowupAsync, cancellationToken);

    internal static Task UnknownRootAsync(CancellationToken cancellationToken)
        => RunOwnedAsync(PathMapFixture.UnknownMap, VerifyUnknownRootAsync, cancellationToken);

    internal static Task EscapingRootAsync(CancellationToken cancellationToken)
        => RunOwnedAsync(PathMapFixture.EscapingMap, VerifyEscapingRootAsync, cancellationToken);

    private static async Task RunOwnedAsync(string mappedRoot,
        Func<PathMapFixture, CancellationToken, Task> verify, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PathMapFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new PathMapFixture(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await PathMapCompiler.CompileAsync(fixture, mappedRoot, cancellationToken);
                await verify(fixture, cancellationToken);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyTamperAndFollowupAsync(PathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var originalDll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var originalPdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        var accepted = await PathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await PathMapAssertions.CanonicalBindingAsync(fixture, accepted, cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(fixture.SourcePath, PathMapFixture.ChangedSourceBytes, cancellationToken);
            var rejected = await PathMapIdentityReader.ReadAsync(fixture, cancellationToken);
            await AssertSafeRejectionAsync(rejected, SourceDrift);
            await Assert.That((await File.ReadAllBytesAsync(fixture.SourcePath, cancellationToken)).AsSpan()
                .SequenceEqual(PathMapFixture.ChangedSourceBytes)).IsTrue();
            await PathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(fixture.SourcePath,
            PathMapFixture.OriginalSourceBytes, CancellationToken.None), failures).ConfigureAwait(false);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        var restored = await PathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await PathMapAssertions.CanonicalBindingAsync(fixture, restored, cancellationToken);
        await PathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
    }

    private static async Task VerifyUnknownRootAsync(PathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var originalDll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var originalPdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        var unbound = await PathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await PathMapAssertions.UnknownRootUnboundAsync(unbound);
        await PathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
    }

    private static async Task VerifyEscapingRootAsync(PathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var originalDll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var originalPdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        var rejected = await PathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await AssertSafeRejectionAsync(rejected, EscapingPath);
        await Assert.That(rejected.StandardError.Contains(PathMapFixture.EscapingMap, StringComparison.Ordinal)).IsFalse();
        await PathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
    }

    private static async Task AssertSafeRejectionAsync(ProductionSourceManifestProcessResult result,
        string expectedFailure)
    {
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError.Trim()).IsEqualTo(expectedFailure);
        await PathMapAssertions.SettledAsync(result);
    }
}
