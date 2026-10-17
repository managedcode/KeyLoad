using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPreparedPageFlow
{
    internal static async Task ExecuteAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await NativeDropRuntimeSeed.CreateAsync(profile, token).ConfigureAwait(false);
        var publicId = Guid.NewGuid();
        var request = new DropTableRequest(NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database,
            NativeDropRuntimeProtocol.Table);
        var lookup = new ResourceRetirementDropParentLookupV1(ResourceRetirementProtocol.Version, publicId, request);
        var intentId = ResourceRetirementDropParentIdentity.CommandId(lookup, ResourceRetirementPhase.PersistDropParent);
        var intent = profile.Observer.Hold(intentId);
        var call = NativeDropPublicSubmission.Start(profile, request, publicId, token);
        var original = await NativeDropPreparedPageProducer.CaptureAsync(profile, lookup, intent, call, token).ConfigureAwait(false);
        _ = await NativeDropPreparedPageExecution.ExecuteAsync(profile, original, call.ExpiresAt, token).ConfigureAwait(false);
        profile.Observer.ReleaseAll();
        var result = NativeDropRuntimeAssertions.Value<DropTableResult>(await call.Original.ConfigureAwait(false));
        await Assert.That(result.Receipt.CommandId).IsEqualTo(ResourceRetirementIdentity.PhaseCommandId(
            original.Control.Request, ResourceRetirementPhase.FinalizeCatalog, profile.Hosts.Control.Owner.PhysicalShardId));
        await profile.Observer.JoinSelectedAsync(token).ConfigureAwait(false);
        var completed = ReadParent(profile, lookup, token);
        await ReplayAsync(profile, lookup, completed, result, token).ConfigureAwait(false);
        for (var cold = default(int); cold < NativeDropRuntimeProtocol.ColdReopens; cold++)
        {
            await profile.RestartAsync(token).ConfigureAwait(false);
            await ReplayAsync(profile, lookup, completed, result, token).ConfigureAwait(false);
        }
    }

    private static ResourceRetirementDropParentV1 ReadParent(NativeDropRuntimeProfile profile,
        ResourceRetirementDropParentLookupV1 lookup, CancellationToken token)
        => profile.Source.Database.ReadResourceRetirementDropParent(PartitionStoreProtocol.AdministratorId, lookup,
            NativeDropPreparedPageProducer.Work(profile, token))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, NativeDropRuntimeProtocol.MissingReply);

    private static async Task ReplayAsync(NativeDropRuntimeProfile profile, ResourceRetirementDropParentLookupV1 lookup,
        ResourceRetirementDropParentV1 completed, DropTableResult expected, CancellationToken token)
    {
        var cuts = profile.Hosts.Nodes.Select(node => node.Log.State.LastIndex).ToArray();
        var call = NativeDropPublicSubmission.Start(profile, lookup.Request, lookup.CommandId, token);
        var replay = NativeDropRuntimeAssertions.Value<DropTableResult>(await call.Original.ConfigureAwait(false));
        await NativeDropRuntimeAssertions.SameAsync(expected, replay).ConfigureAwait(false);
        var retained = ReadParent(profile, lookup, token);
        await NativeDropRuntimeAssertions.SameAsync(completed, retained).ConfigureAwait(false);
        await Assert.That(retained.OriginalIntent.NativePayload.Span.SequenceEqual(completed.OriginalIntent.NativePayload.Span)).IsTrue();
        await Assert.That(retained.OriginalCompletion!.NativePayload.Span.SequenceEqual(completed.OriginalCompletion!.NativePayload.Span)).IsTrue();
        for (var node = default(int); node < profile.Hosts.Nodes.Count; node++)
        {
            await Assert.That(profile.Hosts.Nodes[node].Log.State.LastIndex).IsEqualTo(cuts[node]);
        }
    }
}
