using KeyLoad.UnitTests.Features.CodeQuality.Helpers;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Cases;

internal sealed class NativeProcessEvidenceWholeFlowTests
{
    [Test]
    public async Task AcCq046ActualFailureProjectionPreservesOriginalExceptionsThenHealthyChildAndFullManifestSettle()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var result = await NativeProcessEvidenceWholeFlow.RunAsync(token).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.StandardOutput).IsEqualTo(NativeProcessEvidenceWholeFlow.SuccessMarker);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
        await ProductionSourceManifestScenario.RunOwnedAsync(token).ConfigureAwait(false);
    }
}
