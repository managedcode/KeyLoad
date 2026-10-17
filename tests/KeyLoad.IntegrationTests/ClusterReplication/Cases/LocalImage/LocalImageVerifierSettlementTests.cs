using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication.Cases;

/// <summary>Exercises the existing verifier process boundary, not Docker image provenance or RF3 qualification.</summary>
internal sealed class LocalImageVerifierSettlementTests
{
    private const string Tag = "local-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Receipt = "TestResults/rf3/local-images/image-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json";
    private const string RootPrefix = "keyload-verifier-settlement-";

    [Test]
    [Arguments(LocalImageVerifierProcessProof.SuccessMode)]
    [Arguments(LocalImageVerifierProcessProof.ErrorMode)]
    public async Task AcTest015OriginalExitAndBothStreamResultsDetermineVerification(string mode)
    {
        await LocalImageTestDirectory.RunAsync(RootPrefix, async root =>
        {
            var files = await LocalImageVerifierProcessProof.WriteAsync(root, mode).ConfigureAwait(false);
            var verification = LocalRf3ImageIdentity.RunVerifierAsync(root, files.Script, Tag, Receipt,
                TestContext.Current!.Execution.CancellationToken);
            var failure = await OwnedProcessFailureObserver.CaptureAsync(verification).ConfigureAwait(false);
            if (mode == LocalImageVerifierProcessProof.SuccessMode)
            {
                await Assert.That(failure is null).IsTrue();
                await Assert.That(await verification.ConfigureAwait(false)).IsEqualTo(LocalImageVerifierProcessProof.ExpectedOutput);
            }
            else
            {
                await Assert.That(LocalImageVerifierProcessProof.ContainsMessage(failure,
                    LocalImageVerifierProcessProof.VerificationFailure)).IsTrue();
            }
            await LocalImageVerifierProcessProof.AssertStoppedAsync(files, LocalImageVerifierProcessProof.Finished)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task AcTest015OriginalVerifierDeadlineTerminatesAndJoinsItsActualChildAndReaders()
    {
        await LocalImageTestDirectory.RunAsync(RootPrefix, async root =>
        {
            var files = await LocalImageVerifierProcessProof.WriteAsync(root, LocalImageVerifierProcessProof.WaitMode)
                .ConfigureAwait(false);
            var phases = new List<LocalImageVerifierPhase>();
            var failure = await OwnedProcessFailureObserver.CaptureAsync(LocalRf3ImageIdentity.RunVerifierAsync(
                root, files.Script, Tag, Receipt, phases.Add, CancellationToken.None)).ConfigureAwait(false);
            await Assert.That(LocalImageVerifierProcessProof.ContainsCancellation(failure)).IsTrue();
            await Assert.That(failure is OperationCanceledException).IsTrue();
            await LocalImageVerifierProcessProof.AssertStoppedAsync(files, LocalImageVerifierProcessProof.Terminated)
                .ConfigureAwait(false);
            await Assert.That(phases.SequenceEqual([LocalImageVerifierPhase.BuildInputs])).IsTrue();
            var healthy = await LocalImageVerifierProcessProof.WriteAsync(root, LocalImageVerifierProcessProof.SuccessMode)
                .ConfigureAwait(false);
            var output = await LocalRf3ImageIdentity.RunVerifierAsync(root, healthy.Script, Tag, Receipt, phases.Add,
                TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
            await Assert.That(output).IsEqualTo(LocalImageVerifierProcessProof.ExpectedOutput);
            await Assert.That(phases.SequenceEqual([LocalImageVerifierPhase.BuildInputs])).IsTrue();
            await LocalImageVerifierProcessProof.AssertStoppedAsync(healthy, LocalImageVerifierProcessProof.Finished)
                .ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
