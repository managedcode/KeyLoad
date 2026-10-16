using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Search;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Native cohort setup and independent receipt replay against the original journal owner.</summary>
internal static class RequestCqrsHostedMaintenanceData
{
    private const string ProjectionDirectory = "bilingual-native-text";

    internal static async Task<T> ExecuteAsync<T>(RequestCqrsHostedDatabase database,
        OperationKind kind, object request, Guid commandId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = database.Store.Position;
        var result = database.Submit(kind, request, RequestCqrsHostedProfile.Root,
            commandId, null, cancellationToken).Get<T>();
        var after = database.Store.Position;
        var replay = database.Submit(kind, request, RequestCqrsHostedProfile.Root,
            commandId, null, cancellationToken).Get<T>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(after);
        await Assert.That(after).IsGreaterThanOrEqualTo(before);
        return result;
    }

    internal static async Task<TextIndexMaintenanceRequest> RequestAsync(
        RequestCqrsHostedDatabase fixture, CancellationToken cancellationToken)
    {
        var database = fixture;
        var consumer = new ProjectionConsumerRef(database.Partition, NativeTextMaintenanceTestValues.Consumer);
        var configureId = Guid.NewGuid();
        _ = await ExecuteAsync<ProjectionConsumerInfo>(fixture,
            OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(configureId, consumer,
                new(NativeTextMaintenanceTestValues.Generation, [NativeTextBilingualAudit.Collection],
                    [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
                        MutationDiscriminatorNames.DeleteDocument])), configureId, cancellationToken);
        var placement = database.Database.ReadAtomicPartitionPlacement(NativeTextMaintenanceTestValues.Principal,
            new(NativeTextMaintenanceTestValues.PlacementVersion, database.Partition));
        return new(Guid.NewGuid(), consumer, NativeTextBilingualAudit.Collection, NativeTextMaintenanceTestValues.Field,
            NativeTextMaintenanceTestValues.Generation, database.Store.Identity.NodeId,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch),
            TextIndexMaintenanceMode.Build);
    }

    internal static async Task<CommitReceipt> SeedAsync(RequestCqrsHostedDatabase database, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                NativeTextBilingualAudit.UkrainianJson),
                new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId,
                    NativeTextBilingualAudit.EnglishJson)]);
        return await ExecuteAsync<CommitReceipt>(database,
            OperationKind.Batch, command, command.CommandId, token);
    }

    internal static NativeTextProjection Open(RequestCqrsHostedDatabase database)
        => new(Path.Combine(database.Directory, ProjectionDirectory), database.Database.OperationLimitsOptions,
            database.Store.Identity.NodeId, UnitNativeTextOptions.Execution());
}
