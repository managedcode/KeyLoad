using System.Text.Json;
using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication;
using static KeyLoad.UnitTests.Messaging.RemoteTransferCapacityAuthorityProtocol;

namespace KeyLoad.UnitTests.Messaging;

internal static class RemoteTransferCapacityAuthorityWholeFlow
{
    internal static async Task RunAsync(bool source, RemoteTransferCapacityAuthorityFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var fixture = new RemoteTransferDatabase();
        var resource = new ConfigureResourceRequest(RemoteTransferDatabase.TenantId,
            RemoteTransferDatabase.DatabaseId, new(Documents, ResourceKind.Collection, RemoteTransferDatabase.Domain));
        var creation = fixture.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(),
            RemoteTransferDatabase.RootPrincipal, TimeProvider.System.GetUtcNow(), JsonSerializer.Serialize(resource, JsonDefaults.Options));
        _ = fixture.Database.Apply(creation).Get<ResourceDefinition>();
        var lifetime = ResourceLifetimeIdentity.Create(creation, resource.TenantId, resource.DatabaseId, resource.Definition);
        List<RemoteTransferCapacityAuthorityCompleted> completed = [];
        foreach (var name in new[] { Original, Second })
        {
            var created = await RemoteTransferCapacityAuthoritySeed.CreateAsync(fixture, name);
            completed.Add(await RemoteTransferCapacityAuthoritySettlement.FinishAsync(fixture, created, completed.Count + First));
        }
        var pending = await RemoteTransferCapacityAuthoritySeed.CreateAsync(fixture, Pending);
        var fresh = RemoteTransferCapacityAuthoritySeed.Fresh(fixture, source, pending);
        var old = source ? completed.First().Transfer.Create : completed.First().Accept;
        var applicationReplay = old with
        {
            CommandId = Guid.NewGuid()
        };
        var identity = fixture.Store.Identity with
        {
            SigningKey = fixture.Store.Identity.SigningKey.ToArray()
        };
        var original = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        var position = fixture.Store.Position;
        var damage = RemoteTransferCapacityAuthorityStorage.Damage(fixture, source, fault);
        var damaged = RemoteTransferCapacityAuthorityStorage.Capture(fixture);
        await RemoteTransferCapacityAuthorityRefusal.RequireAsync(fixture, source, fault, completed.First(),
            applicationReplay, fresh, damaged, position + First);
        fixture.Reopen();
        await RemoteTransferCapacityAuthorityStorage.IdentityAsync(fixture, identity, position + First);
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, damaged, position + First);
        await RemoteTransferCapacityAuthorityRefusal.RequireAsync(fixture, source, fault, completed.First(),
            applicationReplay, fresh, damaged, position + First);
        RemoteTransferCapacityAuthorityStorage.Repair(fixture, damage);
        await RemoteTransferCapacityAuthorityStorage.ImageAsync(fixture, original, position + SecondValue);
        var reconciled = await RemoteTransferCapacityAuthorityValues.SubmitAsync(fixture, applicationReplay,
            source ? null : completed.First().Proof.TargetCommit.Position);
        await ContinueAsync(fixture, source, completed, pending, fresh, (applicationReplay, reconciled), new(lifetime.Incarnation.Value), token);
    }

    private static async Task ContinueAsync(RemoteTransferDatabase fixture, bool source,
        List<RemoteTransferCapacityAuthorityCompleted> completed, RemoteTransferCapacityAuthorityTransfer pending,
        CommandRequest fresh, (CommandRequest Request, Result<object> Result) reconciliation, EntityRefLifetime lifetime, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (source)
        {
            var created = await RemoteTransferCapacityAuthoritySeed.CreateAsync(fixture, RemoteTransferCapacityAuthorityProtocol.Fresh, fresh);
            completed.Add(await RemoteTransferCapacityAuthoritySettlement.FinishAsync(fixture, created, completed.Count + First));
            completed.Add(await RemoteTransferCapacityAuthoritySettlement.FinishAsync(fixture, pending, completed.Count + First));
        }
        else
        {
            completed.Add(await RemoteTransferCapacityAuthoritySettlement.FinishAsync(fixture, pending, completed.Count + First, fresh));
        }
        var document = fixture.Database.GetDocument(RemoteTransferDatabase.RootPrincipal,
            new(source ? fixture.SourcePartition : fixture.DestinationPartition, Documents, FreshDocument), cancellationToken: token);
        await RemoteTransferCapacityAuthorityValues.EqualAsync(document,
            new DocumentResult(new(source ? fixture.SourcePartition : fixture.DestinationPartition, Documents, FreshDocument) { Lifetime = lifetime }, First, Payload, false, []));
        var healthy = await RemoteTransferCapacityAuthoritySeed.CreateAsync(fixture, Healthy);
        completed.Add(await RemoteTransferCapacityAuthoritySettlement.FinishAsync(fixture, healthy, completed.Count + First));
        await RemoteTransferCapacityAuthorityValues.ReplayAsync(fixture, reconciliation.Request, reconciliation.Result);
        await RemoteTransferCapacityAuthorityCold.VerifyAsync(fixture, completed);
        await RemoteTransferCapacityAuthorityValues.ReplayAsync(fixture, reconciliation.Request, reconciliation.Result);
    }
}
