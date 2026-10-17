namespace KeyLoad;

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
