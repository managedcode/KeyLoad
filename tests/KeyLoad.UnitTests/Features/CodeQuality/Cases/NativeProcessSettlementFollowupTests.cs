using System.Diagnostics;
using System.Globalization;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class NativeProcessSettlementFollowupTests
{
    private const string PowerShellCommand = "pwsh";
    private const string NoLogoArgument = "-NoLogo";
    private const string NoProfileArgument = "-NoProfile";
    private const string NonInteractiveArgument = "-NonInteractive";
    private const string CommandArgument = "-Command";
    private const string OversizedOutputPrefix = "[Console]::Out.Write(('x' * ";
    private const string OversizedOutputSuffix = ") -join '')";
    private const string OutputFailure = "The native source-manifest producer process failed.";
    private static readonly TimeProvider DefaultTimeProvider = TimeProvider.System;

    [Test]
    public async Task AcCq045CanceledChildAndOutputOverflowSettleBeforeHealthyOperation()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var child = await NativeSourceManifestChildSettlement.RunAsync(token).ConfigureAwait(false);
        await Assert.That(child.Ready).IsEqualTo(NativeSourceManifestChildSettlement.ReadyMarker);
        await Assert.That(child.PendingBeforeCancellation).IsTrue();
        await Assert.That(child.Caller.IsCancellationRequested).IsTrue();
        await Assert.That(child.Original.IsCompletedSuccessfully).IsTrue();
        await Assert.That(child.Exit.IsCompletedSuccessfully).IsTrue();
        await Assert.That(child.Output.IsCompletedSuccessfully).IsTrue();
        await Assert.That(child.Error.IsCompletedSuccessfully).IsTrue();
        await Assert.That(child.ExitedBeforeDisposal && child.ProcessDisposed && child.OwnedRootDeleted).IsTrue();
        await Assert.That(child.Failures.Count).IsEqualTo(1);
        var cancellation = child.Failures.Single() as TaskCanceledException;
        await Assert.That(cancellation).IsNotNull();
        await Assert.That(cancellation!.CancellationToken).IsEqualTo(child.Caller);
        var projected = Assert.ThrowsExactly<TaskCanceledException>(
            () => NativeCoverageImageNodeSettlement.ThrowFailures([.. child.Failures]));
        await Assert.That(projected).IsSameReferenceAs(cancellation);

        await RejectsRealOversizedChildOutputAsync(token).ConfigureAwait(false);
        await ProductionSourceManifestScenario.RunOwnedAsync(token).ConfigureAwait(false);
    }

    private static async Task RejectsRealOversizedChildOutputAsync(CancellationToken cancellationToken)
    {
        var options = ProductionSourceManifestProcess.CaptureExecutionOptions();
        var outputLength = checked(options.Value.CleanupOutputCharacters + 1);
        var start = new ProcessStartInfo(PowerShellCommand)
        {
            WorkingDirectory = ProductionSourceManifestProcess.RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(NoLogoArgument);
        start.ArgumentList.Add(NoProfileArgument);
        start.ArgumentList.Add(NonInteractiveArgument);
        start.ArgumentList.Add(CommandArgument);
        start.ArgumentList.Add(OversizedOutputPrefix
            + outputLength.ToString(CultureInfo.InvariantCulture) + OversizedOutputSuffix);

        InvalidOperationException? failure = null;
        try
        {
            await ProductionSourceManifestProcess.RunAsync(options, start, DefaultTimeProvider,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException observed)
        {
            failure = observed;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Message).IsEqualTo(OutputFailure);
    }
}
