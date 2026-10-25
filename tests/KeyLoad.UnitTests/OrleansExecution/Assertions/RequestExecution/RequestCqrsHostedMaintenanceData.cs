using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Search;

namespace KeyLoad.UnitTests.OrleansExecution;

/// <summary>Production storage setup and independent persisted outcome replay.</summary>
internal static class RequestCqrsHostedMaintenanceData
{
    private const string ProjectionDirectory = "bilingual-execution-text";

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
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(result))).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(after);
        await Assert.That(after).IsGreaterThanOrEqualTo(before);
        return result;
    }

    internal static async Task<TextIndexMaintenanceRequest> RequestAsync(
        RequestCqrsHostedDatabase fixture, CancellationToken cancellationToken)
    {
        var database = fixture;
        var consumer = new ProjectionConsumerRef(database.Partition, TextMaintenanceTestValues.Consumer);
        var configureId = Guid.NewGuid();
        _ = await ExecuteAsync<ProjectionConsumerInfo>(fixture,
            OperationKind.ConfigureProjectionConsumer, new ConfigureProjectionConsumerRequest(configureId, consumer,
                new(TextMaintenanceTestValues.Generation, [TextBilingualAudit.Collection],
                    [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
                        MutationDiscriminatorNames.DeleteDocument])), configureId, cancellationToken);
        return new(Guid.NewGuid(), consumer, TextBilingualAudit.Collection, TextMaintenanceTestValues.Field,
            TextMaintenanceTestValues.Generation, database.Store.Identity.NodeId,
            new(database.Store.Identity.NodeId, database.Store.Identity.Incarnation),
            TextIndexMaintenanceMode.Build);
    }

    internal static async Task<CommitReceipt> SeedAsync(RequestCqrsHostedDatabase database, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), database.Partition,
            [new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                TextBilingualAudit.UkrainianJson),
                new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId,
                    TextBilingualAudit.EnglishJson)]);
        return await ExecuteAsync<CommitReceipt>(database,
            OperationKind.Batch, command, command.CommandId, token);
    }

    internal static RuntimeTextProjection Open(RequestCqrsHostedDatabase database)
        => new(Path.Combine(database.Directory, ProjectionDirectory),
            database.Store.Identity.NodeId, UnitTextOptions.Execution());
}
