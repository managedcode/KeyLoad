using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class RuntimeJournalBootstrapAdmissionJournalFlow
{
    internal static async Task<RuntimeJournalSnapshot> CreateJournalAsync(TestDatabase database,
        AdmittedCommandInbox inbox, CommandAdmissionGovernor governor, PrincipalRecord journalPrincipal)
    {
        var create = RuntimeJournalBootstrapAdmissionFlow.RuntimeJournalMutationFor(RuntimeJournalAction.Create) with
        {
            JournalName = RuntimeJournalBootstrapAdmissionFlow.JournalName,
            InstanceId = Guid.NewGuid()
        };
        var operation = RuntimeJournalBootstrapAdmissionFlow.CreateNative(database, OperationKind.RuntimeJournal, Guid.NewGuid(), create,
            RuntimeJournalIdentity.ProtectedPrincipalId);
        var command = inbox.Enqueue(operation, journalPrincipal, RuntimeJournalBootstrapAdmissionFlow.PayloadBytes(operation));
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(0);
        var result = await RuntimeJournalBootstrapAdmissionFlow.ApplyNextAsync(database, inbox, command);
        await Assert.That(database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId).Journals)
            .HasSingleItem(entry => entry.JournalName == RuntimeJournalBootstrapAdmissionFlow.JournalName);
        return result.Get<RuntimeJournalMutationResult>().Snapshot!;
    }

    internal static async Task CompleteAppendAndReadAsync(TestDatabase database, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, PrincipalRecord holderPrincipal, PrincipalRecord journalPrincipal,
        RuntimeJournalSnapshot header)
    {
        var data = new byte[] { 4, 2, 9 };
        var mutation = RuntimeJournalBootstrapAdmissionFlow.RuntimeJournalMutationFor(RuntimeJournalAction.Append) with
        {
            JournalName = header.JournalName,
            InstanceId = header.InstanceId,
            OwnerGeneration = header.OwnerGeneration,
            ContentRevision = header.ContentRevision,
            Data = data
        };
        var holderRequest = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(RuntimeJournalBootstrapAdmissionFlow.CollectionName, RuntimeJournalBootstrapAdmissionFlow.AppendDocumentId, RuntimeJournalBootstrapAdmissionFlow.DocumentJson)]);
        var holderOperation = RuntimeJournalBootstrapAdmissionFlow.CreateNative(database, OperationKind.Batch, holderRequest.CommandId, holderRequest);
        var holder = inbox.Enqueue(holderOperation, holderPrincipal, RuntimeJournalBootstrapAdmissionFlow.PayloadBytes(holderOperation));
        var saturated = governor.Snapshot();
        var operation = RuntimeJournalBootstrapAdmissionFlow.CreateNative(database, OperationKind.RuntimeJournal, Guid.NewGuid(), mutation,
            RuntimeJournalIdentity.ProtectedPrincipalId);
        await RuntimeJournalBootstrapAdmissionFlow.AssertResourceExhaustedAsync(inbox, journalPrincipal, operation);
        await Assert.That(governor.Snapshot()).IsEqualTo(saturated);
        await RuntimeJournalBootstrapAdmissionFlow.ApplyNextAsync(database, inbox, holder);
        await Assert.That(database.Database.GetDocument(RuntimeJournalBootstrapAdmissionFlow.RootPrincipalId,
            new(database.Partition, RuntimeJournalBootstrapAdmissionFlow.CollectionName, RuntimeJournalBootstrapAdmissionFlow.AppendDocumentId))!.Json).IsEqualTo(RuntimeJournalBootstrapAdmissionFlow.DocumentJson);
        var command = inbox.Enqueue(operation, journalPrincipal, RuntimeJournalBootstrapAdmissionFlow.PayloadBytes(operation));
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(0);
        var result = await RuntimeJournalBootstrapAdmissionFlow.ApplyNextAsync(database, inbox, command);
        await Assert.That(result.Get<RuntimeJournalMutationResult>().Snapshot!.ContentRevision)
            .IsEqualTo(header.ContentRevision + 1);
        var current = database.Database.GetRuntimeJournalHeader(RuntimeJournalIdentity.ProtectedPrincipalId, header.JournalName)!;
        var page = database.Database.ReadRuntimeJournal(RuntimeJournalIdentity.ProtectedPrincipalId,
            new(header.JournalName, current.InstanceId, current.OwnerGeneration, current.ContentRevision, 0));
        await Assert.That(page.Data.Span.SequenceEqual(data)).IsTrue();
        await Assert.That(page.IsCompleted).IsTrue();
    }
}
