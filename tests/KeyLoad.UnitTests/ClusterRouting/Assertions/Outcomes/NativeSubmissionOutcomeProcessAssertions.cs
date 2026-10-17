using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;
using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeSubmissionOutcomeProcessAssertions
{
    internal static async Task<C1OutcomeInspectionProcessResult> RunAsync(C1OutcomeInspectionFixture fixture,
        C1SubmissionOutcomeInspectionRequest request)
    {
        fixture.CloseStore();
        var result = await C1OutcomeInspectionProcess.RunSubmissionOutcomeAsync(
            C1SubmissionOutcomeInspectionProtocol.SerializeRequest(request), fixture.OuterOwnerLockPath,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        C1OutcomeInspectionOwnerPhase.ObserveChild(fixture, result);
        await C1OutcomeInspectionAssertions.AssertJoinedAsync(result).ConfigureAwait(false);
        await RequireLocksReleasedAsync(fixture).ConfigureAwait(false);
        return result;
    }

    private static async Task RequireLocksReleasedAsync(C1OutcomeInspectionFixture fixture)
    {
        const int OwnerProbeBufferBytes = 1024;
        using var outer = OfflineRegularFile.Open(fixture.OuterOwnerLockPath, FileAccess.ReadWrite,
            FileShare.None, OwnerProbeBufferBytes);
        using var canonical = OfflineRegularFile.Open(Path.Combine(fixture.DirectoryPath, "owner.lock"),
            FileAccess.ReadWrite, FileShare.None, OwnerProbeBufferBytes);
        await Assert.That(outer.Length).IsEqualTo(0L);
        await Assert.That(canonical.Length).IsEqualTo(0L);
    }

    internal static async Task RequireHealthyAsync(C1OutcomeInspectionFixture fixture,
        NativeSubmissionOutcomeProcessSeed seed)
    {
        var before = NativeSubmissionOutcomeProcessCut.Read(fixture);
        var result = await RunAsync(fixture, seed.Request).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError.Length).IsEqualTo(0);
        await Assert.That(result.StandardOutput.Length <= C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes).IsTrue();
        var receipt = C1SubmissionOutcomeInspectionProtocol.DeserializeReceipt(result.StandardOutput);
        await Assert.That(receipt.Version).IsEqualTo(C1SubmissionOutcomeInspectionProtocol.Version);
        await Assert.That(receipt.NodeId).IsEqualTo(fixture.Identity.NodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(fixture.Incarnation);
        await Assert.That(receipt.FormatVersion).IsEqualTo(fixture.Identity.FormatVersion);
        await Assert.That(receipt.Position).IsEqualTo(before.Position);
        await seed.RequireOutcomeAsync(receipt).ConfigureAwait(false);
        await before.RequireUnchangedAsync(fixture).ConfigureAwait(false);
    }

    internal static async Task RequireRejectedAsync(C1OutcomeInspectionFixture fixture,
        C1SubmissionOutcomeInspectionRequest request, C1OutcomeInspectionFailurePhase phase)
    {
        var before = NativeSubmissionOutcomeProcessCut.Read(fixture);
        var result = await RunAsync(fixture, request).ConfigureAwait(false);
        await C1OutcomeInspectionAssertions.AssertRejectedAsync(result, phase).ConfigureAwait(false);
        await before.RequireUnchangedAsync(fixture).ConfigureAwait(false);
    }
}
