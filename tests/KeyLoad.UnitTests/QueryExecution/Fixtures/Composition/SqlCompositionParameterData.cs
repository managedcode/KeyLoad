using System.Text.Json;
using C = KeyLoad.UnitTests.Features.DatabaseComposition.QueueGraphIdentityState;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed record SqlCompositionParameterOriginal(CommandRequest Command, ReplicatedOperation Operation,
    CommitReceipt Expected, Mutation Effect);

internal static class SqlCompositionParameterData
{
    internal const string CommandParameter = "command";
    internal const string QueueParameter = "queue";
    internal const string GraphParameter = "graph";
    internal const string WrongCaseGraphParameter = "Graph";
    internal const string UnusedParameter = "unused";
    internal const string StartParameter = "start";
    internal const string PrefixParameter = "prefix";
    internal const string LimitParameter = "limit";
    internal const string DepthParameter = "depth";
    internal const string VerticesParameter = "vertices";
    internal const string EdgesParameter = "edges";
    internal const string DerivedQueue = "sql-parameter-derived";
    internal const string ReversePrefix = "sql-reverse-";
    internal const string Invalid = "The unified SQL statement or bound arguments are invalid.";
    internal const int ColdCycles = 2;
    internal const int ForwardOperationOffsetSeconds = 2;
    internal const int ReverseOperationOffsetSeconds = 3;
    private const string ForwardSql = "INSERT INTO GRAPH_EDGES (command_id, graph, edge_prefix, max_messages) SELECT @command, @graph, @prefix, @limit FROM QUEUE_MESSAGES(@queue)";
    private const string ReverseSql = "INSERT INTO QUEUE_MESSAGES (command_id, queue, message_prefix) SELECT @command, @queue, @prefix FROM GRAPH_TRAVERSE(@graph, @start, @depth, @vertices, @edges)";

    internal static CommandRequest Forward(SqlCompositionParameterFixture fixture, Guid id)
        => new(id, fixture.Source.Partition, [new QueueToGraph(C.Graph, C.Queue, C.Prefix, 1)]);
    internal static CommandRequest Reverse(SqlCompositionParameterFixture fixture, Guid id)
        => new(id, fixture.Source.Partition, [new GraphToQueueMutation(DerivedQueue, C.Graph, fixture.Seed.From, ReversePrefix, 2, 16, 16)]);
    internal static SqlOperationRequest Request(CommandRequest command, bool reverse)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            [CommandParameter] = JsonSerializer.SerializeToElement(command.CommandId, JsonDefaults.Options)
        };
        if (reverse)
        {
            var mutation = (GraphToQueueMutation)command.Mutations[0];
            Add(values, (QueueParameter, mutation.Queue), (PrefixParameter, mutation.MessageIdPrefix), (GraphParameter, mutation.Graph),
                (StartParameter, mutation.Start), (DepthParameter, mutation.MaxDepth), (VerticesParameter, mutation.MaxVertices), (EdgesParameter, mutation.MaxEdges));
        }
        else
        {
            var mutation = (QueueToGraph)command.Mutations[0];
            Add(values, (GraphParameter, mutation.Graph), (PrefixParameter, mutation.EdgeIdPrefix), (LimitParameter, mutation.MaxMessages), (QueueParameter, mutation.Queue));
        }
        return new(command.Partition, reverse ? ReverseSql : ForwardSql, values);
    }

    internal static Mutation Effect(SqlCompositionParameterFixture fixture, bool reverse)
        => reverse ? new EnqueueMessage(DerivedQueue, ReversePrefix + C.Prefix + C.Message,
            JsonSerializer.Serialize(new QueueGraphLink(fixture.Seed.From, fixture.Seed.To, C.Label, C.AttributesJson), JsonDefaults.Options))
            : new UpsertEdge(C.Graph, C.Prefix + C.Message, fixture.Seed.From, fixture.Seed.To, C.Label, C.AttributesJson, 0L);
    internal static MutationReceipt Receipt(SqlCompositionParameterFixture fixture, bool reverse)
        => new(reverse ? "enqueue" : "upsertEdge", reverse ? DerivedQueue : C.Graph,
            (reverse ? ReversePrefix : string.Empty) + C.Prefix + C.Message, 1L)
        {
            CompositionReferences = [fixture.Seed.From, fixture.Seed.To]
        };

    private static void Add(Dictionary<string, JsonElement> target, params (string Name, object Value)[] values)
    {
        foreach (var value in values)
        {
            target.Add(value.Name, JsonSerializer.SerializeToElement(value.Value, JsonDefaults.Options));
        }
    }
}
