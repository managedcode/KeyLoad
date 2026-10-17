using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryConflict
{
    private const string Conflict = "The command ID was already used with different content.";

    internal static async Task RequireAsync(DatabaseEngine database, IAtomicStore store, SharedDomainRecoveryInput input)
    {
        var scope = SharedDomainCorpus.Create(Tenant, Database)[FirstIndex];
        var command = SharedDomainCorpus.Producer(scope, input.Original.Id, Item);
        command = command with
        {
            Mutations = command.Mutations.SetItem(FirstIndex,
            new PutDocument(scope.Documents!, Item, SecondPayload, NoRevision))
        };
        var changed = input.Original with
        {
            PayloadJson = JsonSerializer.Serialize(command, JsonDefaults.Options)
        };
        var rows = SharedDomainStoredImage.Capture(store);
        var position = store.Position;
        var refused = database.Apply(changed);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(refused.SafeDetail).IsEqualTo(Conflict);
        await Assert.That(refused.Json).IsNull();
        await Assert.That(refused.NativeValue).IsNull();
        await SharedDomainRecoveryImage.SameAsync(store, rows, position);
    }
}
