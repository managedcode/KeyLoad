using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed record GraphStorageExpectedEdge(string Id, string From, string To, string Label, string Json, long Revision);

internal static class GraphStorageReferenceFixture
{
    internal const string Root = "root";
    internal const string Nodes = "nodes";
    internal const string Graph = "links";
    internal const string Events = "events";
    internal const string Queue = "jobs";
    internal const string Label = "link";
    internal const string EmptyJson = "{}";
    internal const string HealthyEdge = "healthy";
    internal const int VertexCount = 5;
    internal const int EdgeCount = 8;
    internal const int Steps = 64;
    internal const int RandomSeed = 22017;

    internal static void Configure(TestDatabase database)
    {
        database.Configure(Nodes, ResourceKind.Collection);
        database.Configure(Graph, ResourceKind.Graph);
        database.Configure(Events, ResourceKind.StreamSet);
        database.Configure(Queue, ResourceKind.WorkQueue);
        database.Commit(Enumerable.Range(0, VertexCount)
            .Select(index => (Mutation)new PutDocument(Nodes, VertexId(index), EmptyJson)).ToArray());
    }

    internal static string VertexId(int index) => "vertex-" + index.ToString(CultureInfo.InvariantCulture);
    internal static string EdgeId(int index) => "edge-" + index.ToString(CultureInfo.InvariantCulture);
    internal static string Attributes(int step) => "{\"step\":" + step.ToString(CultureInfo.InvariantCulture) + "}";
    internal static (string From, string To) Endpoints(int step)
    {
        Span<byte> seed = stackalloc byte[sizeof(int) * 2];
        BinaryPrimitives.WriteInt32LittleEndian(seed, RandomSeed);
        BinaryPrimitives.WriteInt32LittleEndian(seed[sizeof(int)..], step);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(seed, digest);
        return (VertexId(digest[0] % VertexCount), VertexId(digest[1] % VertexCount));
    }
    internal static EntityRef Vertex(PartitionRef partition, string id) => new(partition, Nodes, id);
    internal static CommandRequest Command(PartitionRef partition, params Mutation[] mutations)
        => new(Guid.NewGuid(), partition, [.. mutations]);
    internal static OperationResult Submit(DatabaseEngine owner, CommandRequest command)
        => owner.Apply(new(command.CommandId, OperationKind.Batch, Root, owner.EvaluationClock.GetUtcNow(),
            System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options)));

    internal static DatabaseEngine ReopenedOwner(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(),
            UnitExecutionOptions.DueWork(), UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(),
            UnitExecutionOptions.GraphExecution(), UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), UnitExecutionOptions.TimeSeriesExecution());
}
