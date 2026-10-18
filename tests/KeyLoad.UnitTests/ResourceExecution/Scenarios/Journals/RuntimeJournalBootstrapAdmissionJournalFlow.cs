using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class RuntimeJournalBootstrapAdmissionJournalFlow
{
    internal static async Task RunAsync(TestDatabase database, CommandAdmissionGovernor governor, CancellationToken token)
    {
        var create = RuntimeJournalBootstrapAdmissionOperations.Mutation(RuntimeJournalAction.Create) with
        {
            JournalName = RuntimeJournalBootstrapAdmissionOperations.Journal,
            InstanceId = Guid.NewGuid()
        };
        var operation = RuntimeJournalBootstrapAdmissionOperations.Issue(database, OperationKind.RuntimeJournal,
            create, RuntimeJournalIdentity.ProtectedPrincipalId);
        RuntimeJournalSnapshot header;
        using (RuntimeJournalBootstrapAdmissionOperations.Reserve(database, governor, operation, token))
        {
            await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
            await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(0);
            var result = await RuntimeJournalBootstrapAdmissionOperations.ApplyAsync(database, operation, token);
            header = result.Get<RuntimeJournalMutationResult>().Snapshot!;
            await Assert.That(database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId, token).Journals)
                .HasSingleItem(entry => entry.JournalName == RuntimeJournalBootstrapAdmissionOperations.Journal);
        }
        await AppendAsync(database, governor, header, token);
    }

    private static async Task AppendAsync(TestDatabase database, CommandAdmissionGovernor governor,
        RuntimeJournalSnapshot header, CancellationToken token)
    {
        byte[] content = [4, 2, 9];
        var append = RuntimeJournalBootstrapAdmissionOperations.Mutation(RuntimeJournalAction.Append) with
        {
            JournalName = header.JournalName,
            InstanceId = header.InstanceId,
            OwnerGeneration = header.OwnerGeneration,
            ContentRevision = header.ContentRevision,
            Data = content
        };
        var operation = RuntimeJournalBootstrapAdmissionOperations.Issue(database, OperationKind.RuntimeJournal,
            append, RuntimeJournalIdentity.ProtectedPrincipalId);
        var holder = RuntimeJournalBootstrapAdmissionOperations.DocumentCommand(database, RuntimeJournalBootstrapAdmissionOperations.AppendDocument);
        using (RuntimeJournalBootstrapAdmissionOperations.Reserve(database, governor, holder, token))
        {
            await RuntimeJournalBootstrapAdmissionOperations.RefusedAsync(database, governor, operation, token);
            await RuntimeJournalBootstrapAdmissionOperations.ApplyAsync(database, holder, token);
            await RuntimeJournalBootstrapAdmissionOperations.DocumentAsync(database, RuntimeJournalBootstrapAdmissionOperations.AppendDocument);
        }
        using (RuntimeJournalBootstrapAdmissionOperations.Reserve(database, governor, operation, token))
        {
            await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
            await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(0);
            var result = await RuntimeJournalBootstrapAdmissionOperations.ApplyAsync(database, operation, token);
            var current = result.Get<RuntimeJournalMutationResult>().Snapshot!;
            await Assert.That(current.ContentRevision).IsEqualTo(header.ContentRevision + 1);
            var actual = database.Database.GetRuntimeJournalHeader(RuntimeJournalIdentity.ProtectedPrincipalId, header.JournalName, token);
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(current))).IsTrue();
            var page = database.Database.ReadRuntimeJournal(RuntimeJournalIdentity.ProtectedPrincipalId,
                new(header.JournalName, current.InstanceId, current.OwnerGeneration, current.ContentRevision, 0), token);
            await Assert.That(page.Data.Span.SequenceEqual(content)).IsTrue();
            await Assert.That(page.IsCompleted).IsTrue();
        }
    }
}
