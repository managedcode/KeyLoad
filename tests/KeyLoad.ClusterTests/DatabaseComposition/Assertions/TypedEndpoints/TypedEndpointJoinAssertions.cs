using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class TypedEndpointJoinAssertions
{
    private const string MissingSeedRecord = "The original typed join seed record is unavailable.";
    private const string MissingJson = "{\"id\":\"m\",\"title\":\"repaired\"}";
    internal const string ContinuedPrefix = "continued-";
    private const int NoAttempts = 0;

    internal static async Task<TypedEndpointJoinModels> CaptureAsync(RequestCqrsClusterCallers admin,
        PartitionRef partition, CancellationToken token)
    {
        var first = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(TypedEndpointJoinProtocol.From(partition), token));
        var second = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(TypedEndpointJoinProtocol.To(partition), token));
        var hidden = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(
            new(partition, TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.Hidden), token));
        var missing = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(
            new(partition, TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.Missing), token));
        var sources = ImmutableArray.CreateBuilder<MessageInspection>();
        foreach (var id in TypedEndpointJoinProtocol.Messages)
        {
            var message = await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(
                new(new(partition, TypedEndpointJoinProtocol.Queue), id), token));
            sources.Add(message ?? throw new InvalidOperationException(MissingSeedRecord));
        }
        var graph = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.TraverseAsync(
            new(partition, TypedEndpointJoinProtocol.Graph, TypedEndpointJoinProtocol.From(partition)), token));
        await SqlClusterProtocol.EqualAsync(graph, await SqlClusterProtocol.McpAsync<global::KeyLoad.GraphTraversal>(admin.Mcp,
            SqlClusterProtocol.Call(partition, McpCallerTools.GraphTraverse,
                new TraverseRequest(partition, TypedEndpointJoinProtocol.Graph, TypedEndpointJoinProtocol.From(partition))), token));
        var hiddenRecord = hidden ?? throw new InvalidOperationException(MissingSeedRecord);
        var hiddenGraph = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.TraverseAsync(
            new(partition, TypedEndpointJoinProtocol.Graph, hiddenRecord.Reference), token));
        var missingGraph = missing is null ? null : await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.TraverseAsync(
            new(partition, TypedEndpointJoinProtocol.Graph, missing.Reference), token));
        var malformed = await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(
            new(new(partition, TypedEndpointJoinProtocol.EmptyQueue), TypedEndpointJoinProtocol.MalformedMessage), token));
        return new(first ?? throw new InvalidOperationException(MissingSeedRecord),
            second ?? throw new InvalidOperationException(MissingSeedRecord),
            hiddenRecord, missing,
            sources.ToImmutable(), graph, hiddenGraph, missingGraph, malformed);
    }

    internal static async Task LiteralAsync(TypedEndpointJoinModels models, TypedEndpointJoinSeed seed,
        bool continued, bool joined)
    {
        var partition = seed.Partition;
        var from = seed.StoredFrom(TypedEndpointJoinProtocol.First);
        var to = seed.StoredTo(TypedEndpointJoinProtocol.Second);
        await RowAsync(models.First, from, TypedEndpointJoinProtocol.FirstJson);
        await RowAsync(models.Second, to, TypedEndpointJoinProtocol.SecondJson);
        await RowAsync(models.Hidden, seed.StoredFrom(TypedEndpointJoinProtocol.Hidden), TypedEndpointJoinProtocol.HiddenJson);
        await MalformedSourceAsync(models, partition, continued);
        await Assert.That(models.Sources.Length).IsEqualTo(TypedEndpointJoinProtocol.SourceCount);
        for (var index = 0; index < TypedEndpointJoinProtocol.Messages.Length; index++)
        {
            var source = models.Sources[index];
            await Assert.That(source.Metadata.Id).IsEqualTo(TypedEndpointJoinProtocol.Messages[index]);
            await Assert.That(source.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(source.Metadata.Attempts).IsEqualTo(NoAttempts);
            await Assert.That(source.Metadata.LeaseOwner).IsNull();
            await Assert.That(source.Metadata.NotBefore).IsNull();
            await Assert.That(source.Metadata.ExpiresAt).IsNull();
            await Assert.That(source.Metadata.LeaseUntil).IsNull();
            await Assert.That(source.Metadata.SafeFailureCode).IsNull();
            await Assert.That(source.HeadersJson).IsEqualTo(TypedEndpointJoinProtocol.Attributes);
            var fromId = index switch
            {
                0 => TypedEndpointJoinProtocol.First,
                1 => TypedEndpointJoinProtocol.Missing,
                _ => TypedEndpointJoinProtocol.Hidden
            };
            var link = new QueueGraphLink(new(partition, TypedEndpointJoinProtocol.FromCollection, fromId),
                TypedEndpointJoinProtocol.To(partition), TypedEndpointJoinProtocol.Label, TypedEndpointJoinProtocol.Attributes);
            await Assert.That(source.PayloadJson).IsEqualTo(DatabaseCompositionClusterPayload.Golden(link));
        }
        var edges = ImmutableArray.CreateBuilder<EdgeRecord>();
        if (continued)
        {
            edges.Add(Edge(ContinuedPrefix + TypedEndpointJoinProtocol.MatchedMessage, from, to));
        }
        if (joined)
        {
            edges.Add(Edge(TypedEndpointJoinProtocol.Prefix + TypedEndpointJoinProtocol.MatchedMessage, from, to));
        }
        await SqlClusterProtocol.EqualAsync(new global::KeyLoad.GraphTraversal(
            joined ? [from, to] : [from], edges.ToImmutable()), models.Graph);
        await SqlClusterProtocol.EqualAsync(new global::KeyLoad.GraphTraversal(
            [seed.StoredFrom(TypedEndpointJoinProtocol.Hidden)], []), models.HiddenGraph);
        if (continued)
        {
            var missing = seed.StoredFrom(TypedEndpointJoinProtocol.Missing);
            await Assert.That(models.Missing).IsNotNull();
            await RowAsync(models.Missing!, missing, MissingJson);
            await SqlClusterProtocol.EqualAsync(new global::KeyLoad.GraphTraversal([missing, to],
                [Edge(ContinuedPrefix + TypedEndpointJoinProtocol.MissingMessage, missing, to)]), models.MissingGraph);
        }
        else
        {
            await Assert.That(models.Missing).IsNull();
            await Assert.That(models.MissingGraph).IsNull();
        }
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers callers,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var sql = TypedEndpointJoinProtocol.Sql(command);
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(callers.Sdk, sql, token));
        await SqlClusterProtocol.EqualAsync(receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(callers.Mcp, sql, token));
        await SqlClusterProtocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token)));
    }

    internal static PutDocument RepairMissing(TypedEndpointJoinState state)
        => new(TypedEndpointJoinProtocol.FromCollection, TypedEndpointJoinProtocol.Missing, MissingJson,
            Access: new(OwnerId: state.Seed.Principal.Id));

    private static EdgeRecord Edge(string id, EntityRef from, EntityRef to)
        => new(id, from, to, TypedEndpointJoinProtocol.Label, TypedEndpointJoinProtocol.Attributes,
            TypedEndpointJoinProtocol.FirstRevision);

    private static async Task RowAsync(DocumentResult actual, EntityRef reference, string json)
    {
        await Assert.That(actual.Reference).IsEqualTo(reference);
        await Assert.That(actual.Revision).IsEqualTo(TypedEndpointJoinProtocol.FirstRevision);
        await Assert.That(actual.Json).IsEqualTo(json);
        await Assert.That(actual.Redacted).IsFalse();
        await Assert.That(actual.RedactedFields).IsEmpty();
    }

    private static async Task MalformedSourceAsync(TypedEndpointJoinModels models, PartitionRef partition, bool continued)
    {
        if (!continued)
        {
            await Assert.That(models.MalformedSource).IsNull();
            return;
        }
        await Assert.That(models.MalformedSource).IsNotNull();
        var actual = models.MalformedSource!;
        await Assert.That(actual.Metadata.Id).IsEqualTo(TypedEndpointJoinProtocol.MalformedMessage);
        await Assert.That(actual.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(actual.Metadata.Attempts).IsEqualTo(NoAttempts);
        await Assert.That(actual.Metadata.LeaseOwner).IsNull();
        await Assert.That(actual.Metadata.LeaseUntil).IsNull();
        await Assert.That(actual.Metadata.NotBefore).IsNull();
        await Assert.That(actual.Metadata.ExpiresAt).IsNull();
        await Assert.That(actual.Metadata.SafeFailureCode).IsNull();
        await Assert.That(actual.HeadersJson).IsEqualTo(TypedEndpointJoinProtocol.Attributes);
        var to = TypedEndpointJoinProtocol.To(partition);
        await Assert.That(actual.PayloadJson).IsEqualTo(DatabaseCompositionClusterPayload.Golden(
            new QueueGraphLink(to, to, TypedEndpointJoinProtocol.Label, TypedEndpointJoinProtocol.Attributes)));
    }
}
