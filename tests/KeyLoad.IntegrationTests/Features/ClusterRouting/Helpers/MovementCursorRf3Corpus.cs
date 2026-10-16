using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MovementCursorRf3Corpus
{
    internal const string Collection = "movementcursorrows";
    internal const string Index = "by-scope";
    internal const string IndexPath = "index:by-scope";
    internal const string First = "a";
    internal const string Second = "b";
    internal const string FirstJson = "{\"scope\":\"current\",\"order\":1}";
    internal const string SecondJson = "{\"scope\":\"current\",\"order\":2}";
    private const int CompleteRows = 2;
    private const string ScopePath = "/scope";
    private const string CurrentScope = "current";
    private const string PageSql = "SELECT * FROM movementcursorrows WHERE scope = 'current' ORDER BY id LIMIT 1";

    internal static QueryRequest Page(PartitionRef partition, string? cursor = null)
        => new(partition, PageSql, Cursor: cursor);

    internal static AstQueryRequest Live(PartitionRef partition)
        => new(partition, new SelectQuery(Collection, null, [new("*", "*")],
            new Comparison(new FieldOperand(ScopePath), "=", ValueOperand.Create(CurrentScope)), [], CompleteRows));

    internal static async Task<MovementCursorRf3Original> SeedAsync(
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, seed.Partition.TransactionDomainId)
        {
            Indexes = [new(Index, [ScopePath])]
        };
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigureResourceAsync(Guid.NewGuid(),
                new(seed.Partition.TenantId, seed.Partition.DatabaseId, definition), token)));
        var command = seed.Models.Command(new PutDocument(Collection, First, FirstJson),
            new PutDocument(Collection, Second, SecondJson),
            new AppendEvents(KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3Tokens.Streams,
                MovementCursorRf3Stream.StreamId, [MovementCursorRf3Stream.First, MovementCursorRf3Stream.Second],
                ExpectedStreamRevision.NoStream));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(seed.OriginalPlacement.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(seed.OriginalPlacement.PlacementEpoch);
        var pages = await MovementCursorRf3Query.CaptureAsync(seed, token);
        var lives = await MovementCursorRf3Live.CaptureAsync(seed, token);
        var streams = await MovementCursorRf3Stream.CaptureAsync(seed, token);
        return new(command, receipt, pages, lives, streams);
    }

    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        MovementCursorRf3Original original, CancellationToken token)
    {
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(original.Receipt,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original.Command, token)));
        }
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, original.Command, token))).Value);
        var sql = SqlRf3Protocol.Call(seed.Partition, McpCallerTools.DocumentsCommit, original.Command) with
        {
            CommandId = original.Command.CommandId
        };
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            await SqlRf3Protocol.SdkAsync<CommitReceipt>(seed.Source, sql, token));
        await SqlRf3Protocol.EqualAsync(original.Receipt,
            await SqlRf3Protocol.McpAsync<CommitReceipt>(seed.Official, sql, token));
    }
}
