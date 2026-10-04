namespace KeyLoad;

internal static class DatabaseCompositionDefaults
{
    internal const int MaximumItems = 100;
    internal const int MaximumDepth = 3;
    internal const int MaximumVertices = 1_000;
    internal const string EmptyAttributes = "{}";
}

/// <summary>Links two canonical entities in a queue payload or derived graph action.</summary>
/// <param name="From">Identifies the source entity.</param>
/// <param name="To">Identifies the target entity.</param>
/// <param name="Label">Names the graph relationship.</param>
/// <param name="AttributesJson">Contains authorized relationship attributes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueueGraphLink)]
public sealed record QueueGraphLink([property: Orleans.Id(0)] EntityRef From,
    [property: Orleans.Id(1)] EntityRef To, [property: Orleans.Id(2)] string Label,
    [property: Orleans.Id(3)] string AttributesJson = DatabaseCompositionDefaults.EmptyAttributes);

/// <summary>Creates graph edges from all live ready queue links within an explicit bound.</summary>
/// <param name="Graph">Identifies the target graph.</param>
/// <param name="Queue">Identifies the source queue.</param>
/// <param name="EdgeIdPrefix">Prefixes each source message ID to identify its edge.</param>
/// <param name="MaxMessages">Bounds all examined ready entries; overflow rejects the command.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueueToGraph)]
public sealed record QueueToGraph([property: Orleans.Id(0)] string Graph,
    [property: Orleans.Id(1)] string Queue, [property: Orleans.Id(2)] string EdgeIdPrefix,
    [property: Orleans.Id(3)] int MaxMessages = DatabaseCompositionDefaults.MaximumItems) : Mutation(Graph);

/// <summary>Enqueues canonical links selected by a bounded graph traversal.</summary>
/// <param name="Queue">Identifies the target queue.</param>
/// <param name="Graph">Identifies the source graph.</param>
/// <param name="Start">Identifies the canonical start entity.</param>
/// <param name="MessageIdPrefix">Prefixes each source edge ID to identify its message.</param>
/// <param name="MaxDepth">Bounds traversal depth.</param>
/// <param name="MaxVertices">Bounds retained visible entities.</param>
/// <param name="MaxEdges">Bounds examined edges and derived messages.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GraphToQueueMutation)]
public sealed record GraphToQueueMutation([property: Orleans.Id(0)] string Queue,
    [property: Orleans.Id(1)] string Graph, [property: Orleans.Id(2)] EntityRef Start,
    [property: Orleans.Id(3)] string MessageIdPrefix,
    [property: Orleans.Id(4)] int MaxDepth = DatabaseCompositionDefaults.MaximumDepth,
    [property: Orleans.Id(5)] int MaxVertices = DatabaseCompositionDefaults.MaximumVertices,
    [property: Orleans.Id(6)] int MaxEdges = DatabaseCompositionDefaults.MaximumItems) : Mutation(Queue);
