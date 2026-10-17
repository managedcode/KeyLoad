namespace KeyLoad;

/// <summary>Creates graph edges from all live ready queue links within an explicit bound.</summary>
/// <param name="Graph">Identifies the target graph.</param>
/// <param name="Queue">Identifies the source queue.</param>
/// <param name="EdgeIdPrefix">Prefixes each source message ID to identify its edge.</param>
/// <param name="MaxMessages">Bounds all examined ready entries; overflow rejects the command.</param>
/// <param name="FromCollection">Declares the typed source-endpoint collection for an explicit inner join; null preserves ordinary endpoint resolution.</param>
/// <param name="ToCollection">Declares the distinct typed target-endpoint collection; both declarations are required for an inner join.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueueToGraph)]
public sealed record QueueToGraph([property: Orleans.Id(0)] string Graph,
    [property: Orleans.Id(1)] string Queue, [property: Orleans.Id(2)] string EdgeIdPrefix,
    [property: Orleans.Id(3)] int MaxMessages = DatabaseCompositionDefaults.MaximumItems,
    [property: Orleans.Id(4), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? FromCollection = null,
    [property: Orleans.Id(5), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ToCollection = null) : Mutation(Graph);
