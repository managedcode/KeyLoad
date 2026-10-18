using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantShutdownWholeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        TenantAdmissionOperations.Provision(fixture);
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
        await using var inbox = new AdmittedCommandInbox(governor, UnitAdmissionOptions.Inbox());
        string[] ids = ["a1", "b1", "a2", "a3", "healthy-b"];
        var first = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, ids[0]);
        using var selected = TenantAdmissionOperations.Enqueue(fixture, inbox, first, token);
        await Assert.That(await inbox.ReadAsync(token)).IsSameReferenceAs(selected);
        var retained = governor.Snapshot();
        var originals = new[]
        {
            TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalB, ids[1]),
            TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, ids[2]),
            TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, ids[3])
        };
        using var other = TenantAdmissionOperations.Enqueue(fixture, inbox, originals[0], token);
        using var second = TenantAdmissionOperations.Enqueue(fixture, inbox, originals[1], token);
        using var third = TenantAdmissionOperations.Enqueue(fixture, inbox, originals[2], token);
        await StopAsync(fixture, inbox, governor, selected, [other, second, third], originals[0], retained, token);
        var results = new List<OperationResult>();
        var applied = new List<ReplicatedOperation>();
        await ApplyAsync(fixture, selected, ids[0], results, applied, token);
        await inbox.DisposeAsync();
        await using var recovered = new AdmittedCommandInbox(governor, UnitAdmissionOptions.Inbox());
        for (var index = 0; index < originals.Length; index++)
        {
            using var pending = TenantAdmissionOperations.Enqueue(fixture, recovered, originals[index], token);
            await Assert.That(await recovered.ReadAsync(token)).IsSameReferenceAs(pending);
            await ApplyAsync(fixture, pending, ids[index + 1], results, applied, token);
        }
        var healthy = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalB, ids[^1]);
        using var continuation = TenantAdmissionOperations.Enqueue(fixture, recovered, healthy, token);
        await Assert.That(await recovered.ReadAsync(token)).IsSameReferenceAs(continuation);
        await ApplyAsync(fixture, continuation, ids[^1], results, applied, token);
        await TenantAdmissionCold.ReplayAsync(fixture.Database, fixture.Store, fixture.SubmitIssued,
            applied.ToArray(), results, fixture.Partition, token);
        recovered.Stop();
        await recovered.DisposeAsync();
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        fixture.JoinOwnedResources();
        await TenantAdmissionCold.RunAsync(fixture, applied.ToArray(), results, token);
    }

    private static async Task StopAsync(TestDatabase fixture, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, AdmittedCommand selected, AdmittedCommand[] queued,
        ReplicatedOperation original, CommandAdmissionSnapshot retained, CancellationToken token)
    {
        var state = TenantAdmissionAssertions.State(fixture.Store);
        var cut = fixture.ReadJournalCut();
        inbox.Stop();
        foreach (var pending in queued)
        {
            var failure = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => pending.Completion);
            ArgumentNullException.ThrowIfNull(failure);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(failure.StatusCode).IsEqualTo((int)System.Net.HttpStatusCode.ServiceUnavailable);
            await Assert.That(failure.Message).IsEqualTo(CommandAdmissionDetails.UnknownWriteOutcome);
            await Assert.That(failure.InnerException).IsNull();
        }
        await Assert.That(selected.Completion.IsCompleted).IsFalse();
        await Assert.That(governor.Snapshot()).IsEqualTo(retained);
        await Assert.That(await inbox.ReadAsync(token)).IsNull();
        var refused = Assert.ThrowsExactly<KeyLoadException>(() =>
            TenantAdmissionOperations.Enqueue(fixture, inbox, original, token));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(refused.Message).IsEqualTo("The node command queue has stopped accepting operations.");
        await Assert.That(governor.Snapshot()).IsEqualTo(retained);
        await Assert.That(TenantAdmissionAssertions.State(fixture.Store).SequenceEqual(state)).IsTrue();
        await Assert.That(fixture.ReadJournalCut()).IsEqualTo(cut);
    }

    private static async Task ApplyAsync(TestDatabase fixture, AdmittedCommand pending, string id,
        List<OperationResult> results, List<ReplicatedOperation> applied, CancellationToken token)
    {
        var result = fixture.SubmitIssuedEmbedded(pending.Operation, token);
        pending.Complete(result);
        var original = fixture.ReadRetainedEntry(pending.Operation.Id).Operation;
        ArgumentNullException.ThrowIfNull(original);
        await Assert.That(original.Id).IsEqualTo(pending.Operation.Id);
        await Assert.That(original.PrincipalId).IsEqualTo(pending.Operation.PrincipalId);
        await Assert.That(original.PayloadJson).IsEqualTo(pending.Operation.PayloadJson);
        var partition = original.PrincipalId == TenantAdmissionOperations.PrincipalA
            ? fixture.Partition : TenantAdmissionOperations.PartitionB(fixture);
        var position = fixture.ReadJournalCut().CommittedIndex;
        var expectedToken = fixture.Store.Read(view => DatabaseEngine.Token(view, partition, position));
        await TenantAdmissionAssertions.ReceiptAsync(result, original, id, expectedToken);
        await NativeReplayResultAssertions.Same<CommitReceipt>(await pending.Completion, result);
        await TenantAdmissionAssertions.RecordAsync(fixture.Database, original, partition, id);
        results.Add(result);
        applied.Add(original);
    }
}
