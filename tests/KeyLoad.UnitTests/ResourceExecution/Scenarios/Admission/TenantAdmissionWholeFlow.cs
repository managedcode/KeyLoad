using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class TenantAdmissionWholeFlow
{
    private const int TenantCapacity = 3;
    private const int NodeCapacity = 5;

    internal static async Task RunAsync(CancellationToken token)
    {
        using var fixture = new TestDatabase();
        TenantAdmissionOperations.Provision(fixture);
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command(new()
        {
            MaxCommands = NodeCapacity,
            MaxTenantCommands = TenantCapacity,
            MaxPrincipalCommands = TenantCapacity
        }));
        string[] ids = ["a1", "b1", "a2", "a3", "healthy-b"];
        var operations = ids.Select(id => TenantAdmissionOperations.Create(fixture,
            id is "b1" or "healthy-b" ? TenantAdmissionOperations.PrincipalB : TenantAdmissionOperations.PrincipalA, id)).ToArray();
        using var first = TenantAdmissionOperations.Reserve(fixture, governor, operations[0], token);
        using var second = TenantAdmissionOperations.Reserve(fixture, governor, operations[2], token);
        using var third = TenantAdmissionOperations.Reserve(fixture, governor, operations[3], token);
        using var other = TenantAdmissionOperations.Reserve(fixture, governor, operations[1], token);
        await TenantAdmissionRefusals.RunAsync(fixture, governor, token);
        CommandAdmissionLease[] leases = [first, other, second, third];
        var results = new List<Result<object>>();
        var applied = new List<Command<DatabaseCommand>>();
        for (var index = 0; index < leases.Length; index++)
        {
            await ApplyAsync(fixture, operations[index], ids[index], results, applied, token);
            leases[index].Dispose();
        }
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        using (TenantAdmissionOperations.Reserve(fixture, governor, operations[^1], token))
        {
            await ApplyAsync(fixture, operations[^1], ids[^1], results, applied, token);
        }
        await TenantAdmissionCold.ReplayAsync(fixture, fixture.Database, fixture.Store,
            fixture.SubmitIssued, applied.ToArray(), results, fixture.Partition, token);
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        fixture.JoinOwnedResources();
        await TenantAdmissionCold.RunAsync(fixture, applied.ToArray(), results, token);
    }

    private static async Task ApplyAsync(TestDatabase fixture, Command<DatabaseCommand> operation, string id,
        List<Result<object>> results, List<Command<DatabaseCommand>> applied, CancellationToken token)
    {
        var result = fixture.SubmitIssuedEmbedded(operation, token);
        var committed = await TenantAdmissionAssertions.CommittedAsync(fixture, operation);
        var partition = committed.UserId! == TenantAdmissionOperations.PrincipalA
            ? fixture.Partition : TenantAdmissionOperations.PartitionB(fixture);
        var expectedToken = new CommitToken(fixture.Store.Identity.Incarnation, partition.AtomicPartitionId,
            fixture.Database.LastApplied);
        await TenantAdmissionAssertions.ReceiptAsync(result, committed, id, expectedToken);
        await TenantAdmissionAssertions.RecordAsync(fixture, fixture.Database, committed, partition, id);
        results.Add(result);
        applied.Add(committed);
    }
}
