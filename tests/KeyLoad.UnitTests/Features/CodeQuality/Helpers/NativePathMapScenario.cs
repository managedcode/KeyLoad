using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Assertions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal static class NativePathMapScenario
{
    private const int RejectedReaderExitCode = 1;
    private const string SourceDrift = "Compiled source bytes do not match the current source inventory.";
    private const string EscapingPath = "A coverage path is unsafe or escapes the repository.";

    internal static Task CanonicalTamperAndFollowupAsync(CancellationToken cancellationToken)
        => RunOwnedAsync(NativePathMapFixture.CanonicalMap, VerifyTamperAndFollowupAsync, cancellationToken);

    internal static Task UnknownRootAsync(CancellationToken cancellationToken)
        => RunOwnedAsync(NativePathMapFixture.UnknownMap, VerifyUnknownRootAsync, cancellationToken);

    internal static Task EscapingRootAsync(CancellationToken cancellationToken)
        => RunOwnedAsync(NativePathMapFixture.EscapingMap, VerifyEscapingRootAsync, cancellationToken);

    private static async Task RunOwnedAsync(string mappedRoot,
        Func<NativePathMapFixture, CancellationToken, Task> verify, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        NativePathMapFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new NativePathMapFixture(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await NativePathMapCompiler.CompileAsync(fixture, mappedRoot, cancellationToken);
                await verify(fixture, cancellationToken);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
    }

    private static async Task VerifyTamperAndFollowupAsync(NativePathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var originalDll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var originalPdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        var accepted = await NativePathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await NativePathMapAssertions.CanonicalBindingAsync(fixture, accepted, cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(fixture.SourcePath, NativePathMapFixture.ChangedSourceBytes, cancellationToken);
            var rejected = await NativePathMapIdentityReader.ReadAsync(fixture, cancellationToken);
            await AssertSafeRejectionAsync(rejected, SourceDrift);
            await Assert.That((await File.ReadAllBytesAsync(fixture.SourcePath, cancellationToken)).AsSpan()
                .SequenceEqual(NativePathMapFixture.ChangedSourceBytes)).IsTrue();
            await NativePathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(fixture.SourcePath,
            NativePathMapFixture.OriginalSourceBytes, CancellationToken.None), failures).ConfigureAwait(false);
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        var restored = await NativePathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await NativePathMapAssertions.CanonicalBindingAsync(fixture, restored, cancellationToken);
        await NativePathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
    }

    private static async Task VerifyUnknownRootAsync(NativePathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var originalDll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var originalPdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        var unbound = await NativePathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await NativePathMapAssertions.UnknownRootUnboundAsync(unbound);
        await NativePathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
    }

    private static async Task VerifyEscapingRootAsync(NativePathMapFixture fixture,
        CancellationToken cancellationToken)
    {
        var originalDll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var originalPdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        var rejected = await NativePathMapIdentityReader.ReadAsync(fixture, cancellationToken);
        await AssertSafeRejectionAsync(rejected, EscapingPath);
        await Assert.That(rejected.StandardError.Contains(NativePathMapFixture.EscapingMap, StringComparison.Ordinal)).IsFalse();
        await NativePathMapAssertions.ImagesUnchangedAsync(fixture, originalDll, originalPdb, cancellationToken);
    }

    private static async Task AssertSafeRejectionAsync(ProductionSourceManifestProcessResult result,
        string expectedFailure)
    {
        await Assert.That(result.ExitCode).IsEqualTo(RejectedReaderExitCode);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError.Trim()).IsEqualTo(expectedFailure);
        await NativePathMapAssertions.SettledAsync(result);
    }
}
