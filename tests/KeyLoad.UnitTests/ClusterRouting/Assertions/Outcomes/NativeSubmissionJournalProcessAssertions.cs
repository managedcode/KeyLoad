using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeSubmissionJournalProcessAssertions
{
    internal static async Task<C1OutcomeInspectionProcessResult> RunAsync(NativeSubmissionJournalProcessFixture fixture,
        C1SubmissionJournalInspectionRequest request)
    {
        var result = await C1OutcomeInspectionProcess.RunSubmissionJournalAsync(
            C1SubmissionJournalInspectionProtocol.SerializeRequest(request), fixture.Canonical.OuterOwnerLockPath,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        C1OutcomeInspectionOwnerPhase.ObserveChild(fixture.Canonical, result);
        await C1OutcomeInspectionAssertions.AssertJoinedAsync(result).ConfigureAwait(false);
        fixture.RequireLocksReleased();
        return result;
    }

    internal static async Task RequireHealthyAsync(NativeSubmissionJournalProcessFixture fixture)
    {
        var before = NativeSubmissionJournalProcessCut.Read(fixture);
        var result = await RunAsync(fixture, fixture.Request).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError.Length).IsEqualTo(0);
        await Assert.That(result.StandardOutput.Length <= C1SubmissionJournalInspectionProtocol.MaximumReceiptBytes).IsTrue();
        var receipt = C1SubmissionJournalInspectionProtocol.DeserializeReceipt(result.StandardOutput);
        await RequireReceiptAsync(fixture, before, receipt).ConfigureAwait(false);
        await fixture.Seed.RequireCanonicalAsync(fixture.Canonical).ConfigureAwait(false);
        await RequireFullOutcomeAsync(fixture, before).ConfigureAwait(false);
        await before.RequireUnchangedAsync(fixture).ConfigureAwait(false);
    }

    private static async Task RequireReceiptAsync(NativeSubmissionJournalProcessFixture fixture,
        NativeSubmissionJournalProcessCut before, C1SubmissionJournalInspectionReceipt receipt)
    {
        await Assert.That(receipt.Version).IsEqualTo(C1SubmissionJournalInspectionProtocol.Version);
        await Assert.That(receipt.CanonicalNodeId).IsEqualTo(fixture.Canonical.Identity.NodeId);
        await Assert.That(receipt.ReplicaNodeId).IsEqualTo(fixture.ReplicaIdentity.NodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(fixture.Canonical.Incarnation);
        await Assert.That(receipt.CanonicalPosition).IsEqualTo(before.CanonicalPosition);
        await Assert.That(receipt.ReplicaPosition).IsEqualTo(before.ReplicaPosition);
        await Assert.That(receipt.NativeHardState.AsSpan().SequenceEqual(fixture.Seed.NativeState)).IsTrue();
        await Assert.That(receipt.NativeEntry.AsSpan().SequenceEqual(fixture.Seed.NativeEntry)).IsTrue();
        var state = ReplicaProtocolCodec.Deserialize<ReplicaHardState>(receipt.NativeHardState);
        var entry = ReplicaProtocolCodec.Deserialize<ReplicaEntry>(receipt.NativeEntry);
        await Assert.That(state.Version).IsEqualTo(ReplicaProtocol.FormatVersion);
        await Assert.That(state.Incarnation).IsEqualTo(fixture.Canonical.Incarnation);
        await Assert.That(state.LastIndex).IsEqualTo(fixture.Seed.Entry.Index);
        await Assert.That(state.CommittedIndex).IsEqualTo(fixture.Seed.Entry.Index);
        await Assert.That(state.Snapshot).IsNull();
        await Assert.That(entry.Index).IsEqualTo(fixture.Seed.Entry.Index);
        await Assert.That(entry.Term).IsEqualTo(fixture.Seed.Entry.Term);
        await Assert.That(NativeSerialization.Serialize(entry.Operation).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(fixture.Seed.Entry.Operation))).IsTrue();
    }

    private static async Task RequireFullOutcomeAsync(NativeSubmissionJournalProcessFixture fixture,
        NativeSubmissionJournalProcessCut before)
    {
        var seed = fixture.Seed;
        var request = new C1SubmissionOutcomeInspectionRequest(C1SubmissionOutcomeInspectionProtocol.Version,
            fixture.Canonical.DirectoryPath, fixture.Canonical.Identity.NodeId, fixture.Canonical.Incarnation,
            seed.Entry.Operation!.PrincipalId, seed.Command.CommandId, seed.Command.Partition);
        var result = await C1OutcomeInspectionProcess.RunSubmissionOutcomeAsync(
            C1SubmissionOutcomeInspectionProtocol.SerializeRequest(request), fixture.Canonical.OuterOwnerLockPath,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        C1OutcomeInspectionOwnerPhase.ObserveChild(fixture.Canonical, result);
        await C1OutcomeInspectionAssertions.AssertJoinedAsync(result).ConfigureAwait(false);
        fixture.RequireLocksReleased();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError.Length).IsEqualTo(0);
        await Assert.That(result.StandardOutput.Length <= C1SubmissionOutcomeInspectionProtocol.MaximumReceiptBytes).IsTrue();
        var receipt = C1SubmissionOutcomeInspectionProtocol.DeserializeReceipt(result.StandardOutput);
        await Assert.That(receipt.Version).IsEqualTo(C1SubmissionOutcomeInspectionProtocol.Version);
        await Assert.That(receipt.NodeId).IsEqualTo(fixture.Canonical.Identity.NodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(fixture.Canonical.Incarnation);
        await Assert.That(receipt.FormatVersion).IsEqualTo(fixture.Canonical.Identity.FormatVersion);
        await Assert.That(receipt.Position).IsEqualTo(before.CanonicalPosition);
        await Assert.That(receipt.OutcomeKey.AsSpan().SequenceEqual(seed.OutcomeKey)).IsTrue();
        await Assert.That(receipt.NativeStoredOutcome.AsSpan().SequenceEqual(seed.NativeOutcome)).IsTrue();
    }

    internal static async Task RequireRejectedAsync(NativeSubmissionJournalProcessFixture fixture,
        C1SubmissionJournalInspectionRequest request, C1OutcomeInspectionFailurePhase phase)
    {
        var before = NativeSubmissionJournalProcessCut.Read(fixture);
        var result = await RunAsync(fixture, request).ConfigureAwait(false);
        await C1OutcomeInspectionAssertions.AssertRejectedAsync(result, phase).ConfigureAwait(false);
        await fixture.Seed.RequireCanonicalAsync(fixture.Canonical).ConfigureAwait(false);
        await before.RequireUnchangedAsync(fixture).ConfigureAwait(false);
    }
}
