using KeyLoad.Core;
using KeyLoad.Core.Features.RuntimeJournaling.Identity;
using ManagedCode.Communication.Commands;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class RuntimeJournalBootstrapAdmissionFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        using var database = new TestDatabase();
        database.Database.ConfigureRuntimeJournal(Options.Create(new RuntimeJournalOptions()));
        database.Configure(RuntimeJournalBootstrapAdmissionOperations.Collection, ResourceKind.Collection);
        var limits = UnitAdmissionOptions.Command(new()
        {
            MaxCommands = 1,
            MaxTenantCommands = 1,
            MaxPrincipalCommands = 1,
            ReservedControlCommands = 1,
            MaxTenantControlCommands = 1,
            MaxPrincipalControlCommands = 1
        });
        var governor = new CommandAdmissionGovernor(limits);
        await BootstrapAsync(database, governor, limits.Value.MaxControlPayloadBytes, token);
        await RuntimeJournalBootstrapAdmissionJournalFlow.RunAsync(database, governor, token);
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
    }

    private static async Task BootstrapAsync(TestDatabase database, CommandAdmissionGovernor governor,
        int maximumControlBytes, CancellationToken token)
    {
        var mutation = RuntimeJournalBootstrapAdmissionOperations.Mutation(RuntimeJournalAction.BootstrapIdentity);
        var valid = RuntimeJournalBootstrapAdmissionOperations.Issue(database, OperationKind.RuntimeJournal, mutation);
        var malformed = RuntimeJournalBootstrapAdmissionOperations.Issue(database, OperationKind.RuntimeJournal,
            mutation with { JournalName = RuntimeJournalBootstrapAdmissionOperations.Journal });
        var oversized = RuntimeJournalBootstrapAdmissionOperations.Issue(database, OperationKind.RuntimeJournal,
            mutation with { Data = new byte[maximumControlBytes + 1] });
        var original = RuntimeJournalBootstrapAdmissionOperations.DocumentCommand(database,
            RuntimeJournalBootstrapAdmissionOperations.FirstDocument);
        using (RuntimeJournalBootstrapAdmissionOperations.Reserve(database, governor, original, token))
        {
            await RuntimeJournalBootstrapAdmissionOperations.RefusedAsync(database, governor, malformed, token);
            await RuntimeJournalBootstrapAdmissionOperations.RefusedAsync(database, governor, oversized, token);
            await RuntimeJournalBootstrapAdmissionOperations.RefusedAsync(database, governor,
                RuntimeJournalBootstrapAdmissionOperations.DocumentCommand(database, RuntimeJournalBootstrapAdmissionOperations.AppendDocument), token);
            using (RuntimeJournalBootstrapAdmissionOperations.Reserve(database, governor, valid, token))
            {
                await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
                await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(1);
                var duplicate = RuntimeJournalBootstrapAdmissionOperations.Issue(database, OperationKind.RuntimeJournal, mutation);
                await RuntimeJournalBootstrapAdmissionOperations.RefusedAsync(database, governor, duplicate, token);
                var result = await RuntimeJournalBootstrapAdmissionOperations.ApplyAsync(database, valid, token);
                await Assert.That(result.Get<RuntimeJournalMutationResult>().Applied).IsTrue();
                await Assert.That(database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId, token).Journals).IsEmpty();
            }
            await RuntimeJournalBootstrapAdmissionOperations.ApplyAsync(database, original, token);
            await RuntimeJournalBootstrapAdmissionOperations.DocumentAsync(database, RuntimeJournalBootstrapAdmissionOperations.FirstDocument);
        }
        await RejectedMutationAsync(database, governor, malformed, token);
        await RejectedMutationAsync(database, governor, oversized, token);
        await Assert.That(database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId, token).Journals).IsEmpty();
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
    }

    private static async Task RejectedMutationAsync(TestDatabase database, CommandAdmissionGovernor governor,
        Command<DatabaseCommand> operation, CancellationToken token)
    {
        using (RuntimeJournalBootstrapAdmissionOperations.Reserve(database, governor, operation, token))
        {
            var result = database.SubmitIssuedEmbedded(operation, token);
            await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
            await TenantAdmissionAssertions.CommittedAsync(database, operation);
        }
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
    }
}
