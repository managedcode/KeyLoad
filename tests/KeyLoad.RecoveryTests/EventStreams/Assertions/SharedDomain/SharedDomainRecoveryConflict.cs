using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.EventStreams;

internal static class SharedDomainRecoveryConflict
{
    private const string Conflict = "The command ID was already used with different content.";

    internal static async Task RequireAsync(DatabaseEngine database, IAtomicStore store, SharedDomainRecoveryInput input)
    {
        var scope = SharedDomainCorpus.Create(Tenant, Database)[FirstIndex];
        var command = SharedDomainCorpus.Producer(scope, input.Original.CommandId, Item);
        command = command with
        {
            Mutations = command.Mutations.SetItem(FirstIndex,
            new PutDocument(scope.Documents!, Item, SecondPayload, NoRevision))
        };
        var changed = database.CreateCommand(input.Original.Value!.Kind, input.Original.CommandId,
            input.Original.UserId!, new DateTimeOffset(input.Original.Timestamp),
            JsonSerializer.Serialize(command, JsonDefaults.Options));
        var rows = SharedDomainStoredImage.Capture(store);
        var position = store.Position;
        var refused = database.Apply(changed);
        await Assert.That(Errors.Code(refused.Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(refused.Problem?.Detail).IsEqualTo(Conflict);

        await Assert.That(refused.Value).IsNull();
        await SharedDomainRecoveryImage.SameAsync(store, rows, position);
    }
}
