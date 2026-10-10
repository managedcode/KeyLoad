using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxNativePermissions
{
    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, CommitInboxRequest request,
        CommitInboxResult original, CancellationToken token)
    {
        var partition = request.Target.Partition;
        var worker = new PrincipalRecord(TargetInboxUnitProtocol.Worker, partition.TenantId,
            [new(partition.DatabaseId, request.Target.Queue, Capability.InboxWrite),
             new(partition.DatabaseId, TargetInboxUnitProtocol.Collection, Capability.DocumentsWrite),
             new(partition.DatabaseId, TargetInboxUnitProtocol.Events, Capability.EventsAppend),
             new(partition.DatabaseId, TargetInboxUnitProtocol.Output, Capability.QueuePublish)], []);
        _ = database.ApplyEmbedded(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal, TargetInboxUnitProtocol.Root, default,
            System.Text.Json.JsonSerializer.Serialize(new ConfigurePrincipalRequest(worker), JsonDefaults.Options)), token).Get<PrincipalRecord>();
        var duplicate = request with { CommandId = Guid.NewGuid() };
        var replay = TargetInboxNativeSetup.Apply(database, duplicate, token, worker.Id).Get<CommitInboxResult>();
        await Assert.That(replay.AlreadyProcessed).IsTrue();
        await Assert.That(replay.OriginalEffectsToken).IsEqualTo(original.OriginalEffectsToken);
        var demoted = worker with
        {
            PolicyEpoch = TargetInboxUnitProtocol.DemotedEpoch,
            Grants =
            [new(partition.DatabaseId, request.Target.Queue, Capability.InboxWrite)]
        };
        _ = database.ApplyEmbedded(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal, TargetInboxUnitProtocol.Root, default,
            System.Text.Json.JsonSerializer.Serialize(new ConfigurePrincipalRequest(demoted), JsonDefaults.Options)), token).Get<PrincipalRecord>();
        await TargetInboxNativeAssertions.RefusedAsync(database, store, request with { CommandId = Guid.NewGuid() },
            ErrorCode.PermissionDenied, token, worker.Id);
        var restored = worker with { PolicyEpoch = TargetInboxUnitProtocol.RestoredEpoch };
        _ = database.ApplyEmbedded(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal, TargetInboxUnitProtocol.Root, default,
            System.Text.Json.JsonSerializer.Serialize(new ConfigurePrincipalRequest(restored), JsonDefaults.Options)), token).Get<PrincipalRecord>();
        await TargetInboxNativeAssertions.RefusedAsync(database, store, duplicate, ErrorCode.PermissionDenied, token, worker.Id);
        var current = TargetInboxNativeSetup.Apply(database, request with { CommandId = Guid.NewGuid() }, token, worker.Id).Get<CommitInboxResult>();
        await Assert.That(current.AlreadyProcessed).IsTrue();
        await Assert.That(current.OriginalEffectsToken).IsEqualTo(original.OriginalEffectsToken);
        await TargetInboxNativeAssertions.CapacityAsync(store, request, TargetInboxUnitProtocol.FirstRevision);
    }
}
