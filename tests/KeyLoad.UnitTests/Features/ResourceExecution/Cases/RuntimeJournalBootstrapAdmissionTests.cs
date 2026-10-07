using System.Text;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.InternalSerialization;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class RuntimeJournalBootstrapAdmissionTests
{
    [Test]
    public Task ExactBootstrapUsesReservedLaneWhileDataAndOrdinaryJournalEffectsSettle()
        => RuntimeJournalBootstrapAdmissionFlow.RunAsync();
}

internal static class RuntimeJournalBootstrapAdmissionFlow
{
    internal const string RootPrincipalId = "root";
    internal const string CollectionName = "admission-documents";
    internal const string DocumentId = "admitted";
    internal const string AppendDocumentId = "append-admission-holder";
    internal const string JournalName = "admission-journal";
    internal const string DocumentJson = "{\"ready\":true}";

    internal static async Task RunAsync()
    {
        using var database = new TestDatabase();
        database.Database.ConfigureRuntimeJournal(Options.Create(new RuntimeJournalOptions()));
        database.Configure(CollectionName, ResourceKind.Collection);
        var principal = database.Store.Read(view => database.Database.Principal(view, RootPrincipalId,
            database.Database.EvaluationClock.GetUtcNow()));
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
        await using var inbox = new AdmittedCommandInbox(governor, UnitAdmissionOptions.Inbox());

        await VerifyBootstrapAdmissionAsync(database, inbox, governor, principal, limits.Value.MaxControlPayloadBytes);
        var journalPrincipal = database.Store.Read(view => database.Database.Principal(view,
            RuntimeJournalIdentity.ProtectedPrincipalId, database.Database.EvaluationClock.GetUtcNow()));
        var header = await RuntimeJournalBootstrapAdmissionJournalFlow.CreateJournalAsync(database, inbox,
            governor, journalPrincipal);
        await RuntimeJournalBootstrapAdmissionJournalFlow.CompleteAppendAndReadAsync(database, inbox,
            governor, principal, journalPrincipal, header);
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
        inbox.Stop();
    }

    private static async Task VerifyBootstrapAdmissionAsync(TestDatabase database, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, PrincipalRecord principal, int maximumControlPayloadBytes)
    {
        var dataId = Guid.NewGuid();
        var request = new CommandRequest(dataId, database.Partition,
            [new PutDocument(CollectionName, DocumentId, DocumentJson)]);
        var dataOperation = CreateNative(database, OperationKind.Batch, dataId, request);
        var malformed = CreateNative(database, OperationKind.RuntimeJournal, Guid.NewGuid(),
            RuntimeJournalMutationFor(RuntimeJournalAction.BootstrapIdentity) with { JournalName = JournalName });
        var oversized = CreateNative(database, OperationKind.RuntimeJournal, Guid.NewGuid(),
            RuntimeJournalMutationFor(RuntimeJournalAction.BootstrapIdentity) with
            { Data = new byte[maximumControlPayloadBytes + 1] });
        var oversizedNativeWithEmptyJson = new ReplicatedOperation(Guid.NewGuid(), OperationKind.RuntimeJournal,
            RootPrincipalId, database.Database.EvaluationClock.GetUtcNow(), string.Empty)
        { NativePayload = new byte[maximumControlPayloadBytes + 1] };
        var data = inbox.Enqueue(dataOperation, principal, PayloadBytes(dataOperation));
        var saturated = governor.Snapshot();

        await AssertSaturatedCandidatesAsync(database, inbox, governor, principal, request, malformed,
            oversizedNativeWithEmptyJson, oversized, saturated);
        var valid = CreateNative(database, OperationKind.RuntimeJournal, Guid.NewGuid(),
            RuntimeJournalMutationFor(RuntimeJournalAction.BootstrapIdentity), evaluatedAt: dataOperation.EvaluatedAt);
        await AssertPreAdmissionAuthorityRejectionsAsync(database, governor, valid, saturated);
        var control = inbox.Enqueue(valid, principal, PayloadBytes(valid));
        await Assert.That(governor.Snapshot().Commands).IsEqualTo(1);
        await Assert.That(governor.Snapshot().ControlCommands).IsEqualTo(1);
        await AssertResourceExhausted(inbox, database, principal, OperationKind.RuntimeJournal,
            RuntimeJournalMutationFor(RuntimeJournalAction.BootstrapIdentity));
        var bootstrap = await ApplyNextAsync(database, inbox, control);
        await Assert.That(bootstrap.Get<RuntimeJournalMutationResult>().Applied).IsTrue();
        await Assert.That(database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId).Journals)
            .IsEmpty();
        await ApplyNextAsync(database, inbox, data);
        await Assert.That(database.Database.GetDocument(RootPrincipalId,
            new(database.Partition, CollectionName, DocumentId))!.Json).IsEqualTo(DocumentJson);
        await AssertRejectedMutationAsync(database, inbox, governor, principal, malformed);
        await AssertRejectedMutationAsync(database, inbox, governor, principal, oversized);
        await Assert.That(database.Database.ReadRuntimeJournalCatalog(RuntimeJournalIdentity.ProtectedPrincipalId).Journals)
            .IsEmpty();
    }

    private static async Task AssertSaturatedCandidatesAsync(TestDatabase database, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, PrincipalRecord principal, CommandRequest request,
        ReplicatedOperation malformed, ReplicatedOperation oversizedNativeWithEmptyJson,
        ReplicatedOperation oversized, CommandAdmissionSnapshot expected)
    {
        await AssertResourceExhausted(inbox, database, principal, OperationKind.Batch,
            request with { CommandId = Guid.NewGuid() });
        await AssertResourceExhaustedAsync(inbox, principal, malformed);
        await AssertResourceExhaustedAsync(inbox, principal, oversizedNativeWithEmptyJson);
        await AssertResourceExhaustedAsync(inbox, principal, oversized);
        await Assert.That(governor.Snapshot()).IsEqualTo(expected);
    }

    internal static RuntimeJournalMutation RuntimeJournalMutationFor(RuntimeJournalAction action)
        => new(action, string.Empty, Guid.Empty, 0, 0, null, ReadOnlyMemory<byte>.Empty, new(), []);

    internal static ReplicatedOperation CreateNative<T>(TestDatabase database, OperationKind kind, Guid id, T payload,
        string principalId = RootPrincipalId, DateTimeOffset? evaluatedAt = null)
        => database.Database.CreateNativeOperation(kind, id, principalId,
            evaluatedAt ?? database.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(payload));

    internal static int PayloadBytes(ReplicatedOperation operation)
        => Math.Max(Encoding.UTF8.GetByteCount(operation.PayloadJson), operation.NativePayload.Length);

    private static async Task AssertResourceExhausted<T>(AdmittedCommandInbox inbox, TestDatabase database,
        PrincipalRecord principal, OperationKind kind, T payload)
    {
        var operation = CreateNative(database, kind, Guid.NewGuid(), payload);
        await AssertResourceExhaustedAsync(inbox, principal, operation);
    }

    internal static async Task AssertResourceExhaustedAsync(AdmittedCommandInbox inbox,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            inbox.Enqueue(operation, principal, PayloadBytes(operation)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static async Task AssertPreAdmissionAuthorityRejectionsAsync(TestDatabase database,
        CommandAdmissionGovernor governor, ReplicatedOperation operation, CommandAdmissionSnapshot expected)
    {
        var wrongPrincipal = operation with { PrincipalId = NativeAuthorityFixture.OtherPrincipal };
        var wrongPrincipalFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.NormalizeOperation(wrongPrincipal));
        await Assert.That(wrongPrincipalFailure.Code).IsEqualTo(ErrorCode.Corruption);
        var payload = NativeAuthorityFixture.Read(operation);
        var invalidSignature = NativeAuthorityFixture.Wrap(operation, payload with
        { Signature = new byte[NativeAuthorityContract.DigestBytes] });
        var invalidSignatureFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.NormalizeOperation(invalidSignature));
        await Assert.That(invalidSignatureFailure.Code).IsEqualTo(ErrorCode.Corruption);
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span);
        var wrongPurpose = NativeAuthorityFixture.Resign(database, operation,
            claims with { Purpose = NativeAuthorityFixture.WrongPurpose });
        var wrongPurposeFailure = Assert.ThrowsExactly<KeyLoadException>(() =>
            database.Database.NormalizeOperation(wrongPurpose));
        await Assert.That(wrongPurposeFailure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(governor.Snapshot()).IsEqualTo(expected);
    }

    private static async Task AssertRejectedMutationAsync(TestDatabase database, AdmittedCommandInbox inbox,
        CommandAdmissionGovernor governor, PrincipalRecord principal, ReplicatedOperation operation)
    {
        var pending = inbox.Enqueue(operation, principal, PayloadBytes(operation));
        var command = await inbox.ReadAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(command).IsSameReferenceAs(pending);
        var result = database.Database.Apply(command!.Operation);
        command.Complete(result);
        await Assert.That((await command.Completion).Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(governor.Snapshot()).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
    }

    internal static async Task<OperationResult> ApplyNextAsync(TestDatabase database, AdmittedCommandInbox inbox, AdmittedCommand expected)
    {
        var command = await inbox.ReadAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(command).IsSameReferenceAs(expected);
        var result = database.Database.Apply(command!.Operation);
        command.Complete(result);
        await Assert.That((await command.Completion).Error).IsNull();
        return result;
    }
}

internal static class RuntimeJournalBootstrapAdmissionJournalFlow
{
    internal static async Task<RuntimeJournalSnapshot> CreateJournalAsync(TestDatabase database,
        AdmittedCommandInbox inbox, CommandAdmissionGovernor governor, PrincipalRecord journalPrincipal)
    {
        var create = RuntimeJournalBootstrapAdmissionFlow.RuntimeJournalMutationFor(RuntimeJournalAction.Create) with
        { JournalName = RuntimeJournalBootstrapAdmissionFlow.JournalName, InstanceId = Guid.NewGuid() };
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
