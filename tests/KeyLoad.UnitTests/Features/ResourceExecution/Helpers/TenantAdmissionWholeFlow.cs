using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class TenantAdmissionWholeFlow
{
    private const int TenantCapacity = 3;
    private const int NodeCapacity = 5;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase(nativeReplicaAdmission: true);
        TenantAdmissionOperations.Provision(fixture);
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command(new()
        {
            MaxCommands = NodeCapacity,
            MaxTenantCommands = TenantCapacity,
            MaxPrincipalCommands = TenantCapacity
        }));
        await using var inbox = new AdmittedCommandInbox(governor, UnitAdmissionOptions.Inbox());
        var a1 = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "a1");
        var a2 = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "a2");
        var a3 = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalA, "a3");
        var b1 = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalB, "b1");
        using var first = TenantAdmissionOperations.Enqueue(fixture, inbox, a1, token);
        using var second = TenantAdmissionOperations.Enqueue(fixture, inbox, a2, token);
        using var third = TenantAdmissionOperations.Enqueue(fixture, inbox, a3, token);
        using var other = TenantAdmissionOperations.Enqueue(fixture, inbox, b1, token);
        await TenantAdmissionRefusals.RunAsync(fixture, inbox, governor, first, token);
        AdmittedCommand[] expected = [first, other, second, third];
        string[] ids = ["a1", "b1", "a2", "a3"];
        var results = new List<OperationResult>();
        var applied = new List<ReplicatedOperation>();
        for (var index = 0; index < expected.Length; index++)
        {
            var command = await inbox.ReadAsync(token);
            await Assert.That(command).IsSameReferenceAs(expected[index]);
            var result = fixture.SubmitIssuedEmbedded(command!.Operation, token);
            var committed = await TenantAdmissionAssertions.CommittedAsync(fixture, command.Operation);
            applied.Add(committed);
            command.Complete(result);
            var partition = command.Operation.PrincipalId == TenantAdmissionOperations.PrincipalA
                ? fixture.Partition : TenantAdmissionOperations.PartitionB(fixture);
            var position = fixture.ReadJournalCut().CommittedIndex;
            var expectedToken = fixture.Store.Read(view => DatabaseEngine.Token(view, partition, position));
            _ = result.Get<CommitReceipt>();
            await NativeReplayResultAssertions.Same<CommitReceipt>(await command.Completion, result);
            await TenantAdmissionAssertions.ReceiptAsync(result, command.Operation, ids[index], expectedToken);
            await TenantAdmissionAssertions.RecordAsync(fixture.Database, committed, partition, ids[index]);
            results.Add(result);
        }

        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        var healthy = TenantAdmissionOperations.Create(fixture, TenantAdmissionOperations.PrincipalB, "healthy-b");
        using var pending = TenantAdmissionOperations.Enqueue(fixture, inbox, healthy, token);
        await Assert.That(await inbox.ReadAsync(token)).IsSameReferenceAs(pending);
        var healthyResult = fixture.SubmitIssuedEmbedded(healthy, token);
        var committedHealthy = fixture.ReadRetainedEntry(healthy.Id).Operation;
        ArgumentNullException.ThrowIfNull(committedHealthy);
        applied.Add(committedHealthy);
        pending.Complete(healthyResult);
        await TenantAdmissionAssertions.RecordAsync(fixture.Database, committedHealthy,
            TenantAdmissionOperations.PartitionB(fixture), "healthy-b");
        var operations = applied.ToArray();
        results.Add(healthyResult);
        await TenantAdmissionCold.ReplayAsync(fixture.Database, fixture.Store,
            fixture.SubmitIssued, operations, results, fixture.Partition, token);
        inbox.Stop();
        await inbox.DisposeAsync();
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        fixture.JoinOwnedResources();
        await TenantAdmissionCold.RunAsync(fixture, operations, results, token);
    }
}
