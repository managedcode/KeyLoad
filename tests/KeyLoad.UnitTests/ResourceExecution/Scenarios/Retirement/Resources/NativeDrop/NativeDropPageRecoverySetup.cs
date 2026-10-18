using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageRecoverySetup
{
    internal static async Task<NativeDropPageAttemptState> OpenAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await NativeDropRuntimeSeed.CreateAsync(profile, token).ConfigureAwait(false);
        var definition = new ResourceDefinition(NativeDropPageRecoveryData.OtherTable, ResourceKind.Collection,
            NativeDropRuntimeProtocol.Domain)
        {
            RelationalSchema = new(NativeDropRuntimeProtocol.IdField,
                [new(NativeDropRuntimeProtocol.IdField, RelationalColumnType.Text),
                 new(NativeDropRuntimeProtocol.ValueField, RelationalColumnType.Text)])
        };
        _ = NativeDropRuntimeAssertions.Value<ResourceDefinition>(await profile.Source.SubmitAsync(OperationKind.CreateTable,
            Guid.NewGuid(), PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(new CreateTableRequest(
                NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database, definition)), token).ConfigureAwait(false));
        var id = Guid.NewGuid();
        var lookup = new ResourceRetirementDropParentLookupV1(ResourceRetirementProtocol.Version, id,
            new(NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database, NativeDropRuntimeProtocol.Table));
        var hold = profile.Observer.Hold(ResourceRetirementDropParentIdentity.CommandId(lookup, ResourceRetirementPhase.PersistDropParent));
        var call = NativeDropPublicSubmission.Start(profile, lookup.Request, id, token);
        var original = await NativeDropPreparedPageProducer.CaptureAsync(profile, lookup, hold, call, token).ConfigureAwait(false);
        var source = profile.Source;
        var work = NativeDropPreparedPageProducer.Work(profile, token);
        work.ConstrainLifetime(call.ExpiresAt);
        var metadata = profile.ReserveMetadata(work);
        var principal = source.Store.Read(raw => source.Database.Principal(work.CreateView(raw, metadata),
            PartitionStoreProtocol.AdministratorId, source.Clock.GetUtcNow()));
        var context = new NativeDropTableContext(source.Partition, profile.Client, source.Runtime, source.Clock,
            profile.Hosts.Control, principal.Id, principal.PolicyEpoch, call.ExpiresAt, work, metadata, token);
        var entry = original.FinalInventory!.Entries.Single(item => item.Partition == NativeDropRuntimeProtocol.Partition);
        var receiver = original.Control.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId == entry.ObservedOwner.PhysicalShardId);
        var scope = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.CleanupPage,
            original.Control, null, receiver)
        {
            Page = new(entry.Partition, NativeDropRuntimeProtocol.FirstPage)
        };
        return new(profile, call, lookup, original, context, scope, receiver);
    }

    internal static async Task HealthyOtherAsync(NativeDropPageAttemptState state, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, NativeDropRuntimeProtocol.Partition,
            [new PutDocument(NativeDropPageRecoveryData.OtherTable, NativeDropPageRecoveryData.OtherRow,
                NativeDropPageRecoveryData.OtherJson)]);
        var result = await state.Profile.Source.SubmitAsync(OperationKind.Batch, id, PartitionStoreProtocol.AdministratorId,
            NativeSerialization.Serialize(request), token).ConfigureAwait(false);
        await Assert.That(NativeDropRuntimeAssertions.Value<CommitReceipt>(result).CommandId).IsEqualTo(id);
        var row = state.Profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(NativeDropRuntimeProtocol.Partition, NativeDropPageRecoveryData.OtherTable, NativeDropPageRecoveryData.OtherRow), cancellationToken: token);
        await Assert.That(row!.Json).IsEqualTo(NativeDropPageRecoveryData.OtherJson);
    }
}
