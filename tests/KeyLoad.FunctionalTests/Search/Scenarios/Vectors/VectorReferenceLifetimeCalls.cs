using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.Server;
using static KeyLoad.FunctionalTests.Search.VectorReferenceLifetimeProtocol;

namespace KeyLoad.FunctionalTests.Search;

internal static class VectorReferenceLifetimeCalls
{
    internal static async Task SeedAsync(DropRuntimeProfile profile, CancellationToken token)
    {
        await DropRuntimeSeed.CreateAsync(profile, token);
        await ConfigureAsync(profile, new(Target, ResourceKind.Collection, DropRuntimeProtocol.Domain)
        {
            VectorProfiles = [new(Field, Space)]
        }, token);
        await ConfigureAsync(profile, new(StreamSet, ResourceKind.StreamSet, DropRuntimeProtocol.Domain), token);
        _ = await CommitAsync(profile, new(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [new PutDocument(Target, DropRuntimeProtocol.Row, TargetJson)]), token);
        await EventAsync(profile, fresh: false, token);
    }

    internal static async Task EventAsync(DropRuntimeProfile profile, bool fresh, CancellationToken token)
        => _ = await CommitAsync(profile, new(Guid.NewGuid(), DropRuntimeProtocol.Partition,
            [new AppendEvents(StreamSet, VectorReferenceLifetimeProtocol.StreamId, [new(fresh ? FreshEvent : FirstEvent, EventType, EventJson)],
                fresh ? ExpectedStreamRevision.Exact(FirstRevision) : ExpectedStreamRevision.NoStream)]), token);

    internal static async Task<CommitReceipt> CommitAsync(DropRuntimeProfile profile,
        CommandRequest command, CancellationToken token)
        => DropRuntimeAssertions.Value<CommitReceipt>(await DropPublicSubmission.SubmitAsync(profile, OperationKind.Batch,
            command.CommandId, PartitionStoreProtocol.AdministratorId, command, token));

    private static async Task ConfigureAsync(DropRuntimeProfile profile, ResourceDefinition definition,
        CancellationToken token)
    {
        var result = await DropPublicSubmission.SubmitAsync(profile, OperationKind.ConfigureResource, Guid.NewGuid(),
            PartitionStoreProtocol.AdministratorId, new ConfigureResourceRequest(
                DropRuntimeProtocol.Tenant, DropRuntimeProtocol.Database, definition), token);
        await DropRuntimeAssertions.SameAsync(definition, DropRuntimeAssertions.Value<ResourceDefinition>(result));
    }

    internal static EntityRef Source(DropRuntimeProfile profile)
        => profile.Source.Database.GetDocument(PartitionStoreProtocol.AdministratorId,
            new(DropRuntimeProtocol.Partition, DropRuntimeProtocol.Table, DropRuntimeProtocol.Row))!.Reference;

    internal static Task<RankedDocument[]> SearchAsync(DropRuntimeProfile profile, CancellationToken token)
        => new KeyLoad.Query.SearchEngine(profile.Source.Database, UnitExecutionOptions.QueryExecution()).SearchAsync(
            PartitionStoreProtocol.AdministratorId, new(DropRuntimeProtocol.Partition, Target,
                VectorField: Field, Vector: [1, 0], Space: Space), token);
}
