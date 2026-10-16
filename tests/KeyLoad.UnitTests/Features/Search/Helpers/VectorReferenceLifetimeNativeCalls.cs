using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.VectorReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class VectorReferenceLifetimeNativeCalls
{
    internal static async Task SeedAsync(NativeDropRuntimeProfile profile, CancellationToken token)
    {
        await NativeDropRuntimeSeed.CreateAsync(profile, token);
        await ConfigureAsync(profile, new(Target, ResourceKind.Collection, NativeDropRuntimeProtocol.Domain)
        {
            VectorProfiles = [new(Field, Space)]
        }, token);
        await ConfigureAsync(profile, new(StreamSet, ResourceKind.StreamSet, NativeDropRuntimeProtocol.Domain), token);
        _ = await CommitAsync(profile, new(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [new PutDocument(Target, NativeDropRuntimeProtocol.Row, TargetJson)]), token);
        await EventAsync(profile, fresh: false, token);
    }

    internal static async Task EventAsync(NativeDropRuntimeProfile profile, bool fresh, CancellationToken token)
        => _ = await CommitAsync(profile, new(Guid.NewGuid(), NativeDropRuntimeProtocol.Partition,
            [new AppendEvents(StreamSet, StreamId, [new(fresh ? FreshEvent : FirstEvent, EventType, EventJson)],
                fresh ? ExpectedStreamRevision.Exact(FirstRevision) : ExpectedStreamRevision.NoStream)]), token);

    internal static async Task<CommitReceipt> CommitAsync(NativeDropRuntimeProfile profile,
        CommandRequest command, CancellationToken token)
        => NativeDropRuntimeAssertions.Value<CommitReceipt>(await profile.Source.SubmitAsync(OperationKind.Batch,
            command.CommandId, PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(command), token));

    private static async Task ConfigureAsync(NativeDropRuntimeProfile profile, ResourceDefinition definition,
        CancellationToken token)
    {
        var result = await profile.Source.SubmitAsync(OperationKind.ConfigureResource, Guid.NewGuid(),
            PartitionStoreProtocol.AdministratorId, NativeSerialization.Serialize(new ConfigureResourceRequest(
                NativeDropRuntimeProtocol.Tenant, NativeDropRuntimeProtocol.Database, definition)), token);
        await NativeDropRuntimeAssertions.SameAsync(definition, NativeDropRuntimeAssertions.Value<ResourceDefinition>(result));
    }

    internal static EntityRef Source(NativeDropRuntimeProfile profile)
        => profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(NativeDropRuntimeProtocol.Partition, NativeDropRuntimeProtocol.Table, NativeDropRuntimeProtocol.Row))!.Reference;

    internal static Task<RankedDocument[]> SearchAsync(NativeDropRuntimeProfile profile, CancellationToken token)
        => new KeyLoad.Query.SearchEngine(profile.Source.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(
            PartitionStoreProtocol.AdministratorId, new(NativeDropRuntimeProtocol.Partition, Target,
                VectorField: Field, Vector: [1, 0], Space: Space), token);
}
