using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class NativeSourceManifestOutputBoundaryTests
{
    private const int BoundaryStep = 1;

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AcCq044NativePrefixBoundPreservesOriginalSettlementAndHealthyFollowingChild(bool prefixFits)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var prefix = NativeSourceManifestChildSettlement.ReadyMarker + Environment.NewLine;
        var options = new TestExecutionOptions
        {
            CleanupOutputCharacters = prefix.Length - (prefixFits ? 0 : BoundaryStep)
        };
        var bounded = await NativeSourceManifestChildSettlement.RunAsync(Options.Create(options), cancellationToken);

        await AssertCleanupAsync(bounded);
        await Assert.That(bounded.Ready).IsEqualTo(NativeSourceManifestChildSettlement.ReadyMarker);
        await Assert.That(bounded.Output.IsCompletedSuccessfully).IsEqualTo(prefixFits);
        await Assert.That(bounded.Error.IsCompletedSuccessfully).IsEqualTo(prefixFits);
        await Assert.That(bounded.Original.IsCompletedSuccessfully).IsEqualTo(prefixFits);
        await Assert.That(bounded.StandardOutput).IsEqualTo(prefixFits ? prefix : string.Empty);
        await Assert.That(bounded.Failures.Any(failure => failure is InvalidDataException
            && string.Equals(failure.Message, NativeSourceManifestChildSettlement.OutputFailure,
                StringComparison.Ordinal))).IsEqualTo(!prefixFits);
        if (prefixFits)
        {
            await Assert.That(bounded.StandardError).IsEqualTo(
                NativeSourceManifestChildSettlement.ErrorMarker + Environment.NewLine);
        }

        var healthy = await NativeSourceManifestChildSettlement.RunAsync(cancellationToken);
        await AssertCleanupAsync(healthy);
        await Assert.That(healthy.Original.IsCompletedSuccessfully).IsTrue();
        await Assert.That(healthy.StandardOutput).IsEqualTo(prefix);
        await Assert.That(healthy.StandardError).IsEqualTo(
            NativeSourceManifestChildSettlement.ErrorMarker + Environment.NewLine);
        await Assert.That(healthy.Failures.Count).IsEqualTo(1);
        await Assert.That(healthy.Failures.Single() is TaskCanceledException).IsTrue();
    }

    private static async Task AssertCleanupAsync(NativeSourceManifestChildJoined joined)
    {
        await Assert.That(joined.Exit.IsCompletedSuccessfully).IsTrue();
        await Assert.That(joined.Output.IsCompleted).IsTrue();
        await Assert.That(joined.Error.IsCompleted).IsTrue();
        await Assert.That(joined.Original.IsCompleted).IsTrue();
        await Assert.That(joined.ExitedBeforeDisposal).IsTrue();
        await Assert.That(joined.ProcessDisposed).IsTrue();
        await Assert.That(joined.OwnedRootDeleted).IsTrue();
    }
}
