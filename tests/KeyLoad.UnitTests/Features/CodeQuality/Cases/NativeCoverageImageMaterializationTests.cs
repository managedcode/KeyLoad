using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed class NativeCoverageImageMaterializationTests
{
    [Test]
    public async Task RealMaterializerRejectsAlteredAndOccupiedInputsThenCopiesTheObservedReleaseClosure()
    {
        await using var fixture = NativeCoverageImageFixture.Create();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var limits = fixture.Options.Coverage.Value;
            var original = NativeCoverageImageSourceSnapshot.Capture(NativeCoverageImageFixture.ServerOutput,
                limits.MaximumFiles, limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.ReadBufferBytes);
            await RejectsPublicDescriptorAsync(fixture).ConfigureAwait(false);
            await RejectsAlteredCopyAsync(fixture).ConfigureAwait(false);
            await RejectsExistingDestinationAsync(fixture).ConfigureAwait(false);
            await MaterializesFreshContextAsync(fixture).ConfigureAwait(false);
            original.VerifyUnchanged(NativeCoverageImageFixture.ServerOutput, limits.MaximumFiles,
                limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.ReadBufferBytes);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => fixture.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        NativeCoverageImageNodeSettlement.ThrowFailures(failures);
    }

    private static async Task RejectsPublicDescriptorAsync(NativeCoverageImageFixture fixture)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Native coverage image permissions require a Unix filesystem.");
        }
        var context = fixture.ContextPath("public-descriptor-context");
        var invocation = await NativeCoverageImageInvocationFactory.CreateAsync(fixture,
            NativeCoverageImageFixture.ServerOutput, context).ConfigureAwait(false);
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await File.ReadAllBytesAsync(invocation.Path, token).ConfigureAwait(false);
        await Assert.That(File.GetUnixFileMode(invocation.Path))
            .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Assert.ThrowsExactlyAsync<IOException>(() => NativeCoverageRf3InvocationWriter.WriteCreateOnlyAsync(
            invocation.Path, NativeCoverageImageConstants.SentinelBytes, fixture.Options.Coverage.Value.ReadBufferBytes, token));
        await Assert.That((await File.ReadAllBytesAsync(invocation.Path, token).ConfigureAwait(false)).AsSpan().SequenceEqual(original)).IsTrue();
        await Assert.That(File.GetUnixFileMode(invocation.Path))
            .IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var publicMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(invocation.Path, publicMode);
        var result = await RunAsync(fixture, invocation).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(NativeCoverageImageConstants.RejectedExitCode);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).IsEqualTo(NativeCoverageImageConstants.FailureOutput);
        await Assert.That(Directory.Exists(context)).IsFalse();
        await Assert.That(File.GetUnixFileMode(invocation.Path)).IsEqualTo(publicMode);
        await Assert.That((await File.ReadAllBytesAsync(invocation.Path, token).ConfigureAwait(false)).AsSpan().SequenceEqual(original)).IsTrue();
        await Assert.That(result.OriginalExitJoined && result.StandardOutputJoined
            && result.StandardErrorJoined && result.ProcessDisposed).IsTrue();
    }

    private static async Task RejectsAlteredCopyAsync(NativeCoverageImageFixture fixture)
    {
        var alteredDirectory = fixture.CopyServerClosure("altered-server");
        var context = fixture.ContextPath("altered-context");
        var invocation = await NativeCoverageImageInvocationFactory.CreateAsync(fixture, alteredDirectory, context).ConfigureAwait(false);
        fixture.AlterServerAssembly(alteredDirectory);
        var result = await RunAsync(fixture, invocation).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(NativeCoverageImageConstants.RejectedExitCode);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).IsEqualTo(NativeCoverageImageConstants.FailureOutput);
        await Assert.That(Directory.Exists(context)).IsFalse();
        await Assert.That(result.OriginalExitJoined && result.StandardOutputJoined
            && result.StandardErrorJoined && result.ProcessDisposed).IsTrue();
    }

    private static async Task RejectsExistingDestinationAsync(NativeCoverageImageFixture fixture)
    {
        var context = fixture.ContextPath("occupied-context");
        var invocation = await NativeCoverageImageInvocationFactory.CreateAsync(fixture, NativeCoverageImageFixture.ServerOutput, context).ConfigureAwait(false);
        Directory.CreateDirectory(context);
        var contextMode = NativeCoverageImageOracleSupport.Mode(context);
        var sentinel = Path.Combine(context, NativeCoverageImageConstants.SentinelName);
        await File.WriteAllBytesAsync(sentinel, NativeCoverageImageConstants.SentinelBytes,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        var sentinelMode = NativeCoverageImageOracleSupport.Mode(sentinel);
        var result = await RunAsync(fixture, invocation).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(NativeCoverageImageConstants.RejectedExitCode);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).IsEqualTo(NativeCoverageImageConstants.FailureOutput);
        var preserved = await File.ReadAllBytesAsync(sentinel,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        await Assert.That(preserved).IsEquivalentTo(NativeCoverageImageConstants.SentinelBytes);
        await Assert.That(NativeCoverageImageOracleSupport.Mode(sentinel)).IsEqualTo(sentinelMode);
        await Assert.That(NativeCoverageImageOracleSupport.Mode(context)).IsEqualTo(contextMode);
        await Assert.That(Directory.EnumerateFiles(context, "*", SearchOption.AllDirectories).Count()).IsEqualTo(1);
        await Assert.That(result.OriginalExitJoined && result.StandardOutputJoined
            && result.StandardErrorJoined && result.ProcessDisposed).IsTrue();
    }

    private static async Task MaterializesFreshContextAsync(NativeCoverageImageFixture fixture)
    {
        var invocation = await NativeCoverageImageInvocationFactory.CreateAsync(fixture, NativeCoverageImageFixture.ServerOutput,
            fixture.ContextPath("fresh-context")).ConfigureAwait(false);
        var result = await RunAsync(fixture, invocation).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).IsEmpty();
        await Assert.That(result.OriginalExitJoined && result.StandardOutputJoined
            && result.StandardErrorJoined && result.ProcessDisposed).IsTrue();
        NativeCoverageImageContextOracle.Verify(fixture, invocation, result);
        await NativeCoverageImagePreparationFlow.VerifyTamperAndHealthyAsync(fixture, invocation, result)
            .ConfigureAwait(false);
    }

    private static Task<NativeCoverageImageNodeResult> RunAsync(NativeCoverageImageFixture fixture,
        NativeCoverageImageInvocation invocation)
        => NativeCoverageImageNodeProcess.RunAsync(NativeCoverageImageFixture.RepositoryRoot,
            invocation, fixture.Options.Execution, fixture.Options.Coverage,
            TestContext.Current!.Execution.CancellationToken);
}
