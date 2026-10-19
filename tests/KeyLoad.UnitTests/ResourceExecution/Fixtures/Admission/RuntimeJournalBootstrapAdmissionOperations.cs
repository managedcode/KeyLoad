using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class RuntimeJournalBootstrapAdmissionOperations
{
    internal const string Root = "root";
    internal const string Collection = "admission-documents";
    internal const string FirstDocument = "admitted";
    internal const string AppendDocument = "append-admission-holder";
    internal const string Journal = "admission-journal";
    internal const string DocumentJson = "{\"ready\":true}";

    internal static RuntimeJournalMutation Mutation(RuntimeJournalAction action)
        => new(action, string.Empty, Guid.Empty, 0, 0, null, ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);

    internal static Command<DatabaseCommand> Issue<T>(TestDatabase database, OperationKind kind, T payload,
        string principal = Root, Guid? commandId = null)
        => database.Database.CreateCommand(kind, commandId ?? Guid.NewGuid(), principal,
            database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(payload));

    internal static Command<DatabaseCommand> DocumentCommand(TestDatabase database, string id)
    {
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, database.Partition, [new PutDocument(Collection, id, DocumentJson)]);
        return Issue(database, OperationKind.Batch, request, commandId: commandId);
    }

    internal static CommandAdmissionLease Reserve(TestDatabase database, CommandAdmissionGovernor governor,
        Command<DatabaseCommand> operation, CancellationToken token)
    {
        var principal = database.Store.Read(view => database.Database.Principal(view, operation.UserId!,
            database.Database.EvaluationClock.GetUtcNow()));
        return governor.Reserve(operation, principal, checked((int)OrleansSerialization.Measure(operation)), token);
    }

    internal static async Task RefusedAsync(TestDatabase database, CommandAdmissionGovernor governor,
        Command<DatabaseCommand> operation, CancellationToken token)
    {
        var before = governor.Snapshot();
        var image = TenantAdmissionAssertions.State(database.Store);
        var cut = database.ReadCanonicalCut();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Reserve(database, governor, operation, token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(governor.Snapshot()).IsEqualTo(before);
        await TenantAdmissionAssertions.UnchangedAsync(database, image, cut);
    }

    internal static async Task<Result<object>> ApplyAsync(TestDatabase database, Command<DatabaseCommand> operation,
        CancellationToken token)
    {
        var result = database.SubmitIssuedEmbedded(operation, token);
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Problem).IsNull();
        var stored = await TenantAdmissionAssertions.CommittedAsync(database, operation);
        await Assert.That(stored.CommandId).IsEqualTo(operation.CommandId);
        return result;
    }

    internal static async Task DocumentAsync(TestDatabase database, string id)
    {
        var record = database.Database.GetDocument(Root, new(database.Partition, Collection, id));
        ArgumentNullException.ThrowIfNull(record);
        await Assert.That(record.Json).IsEqualTo(DocumentJson);
        await Assert.That(record.Revision).IsEqualTo(1L);
        await Assert.That(record.Redacted).IsFalse();
    }
}
