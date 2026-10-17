namespace KeyLoad;

/// <summary>Reads the eventual reverse projection for one fully qualified target vertex.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.IncomingRequest)]
public sealed record ReadIncomingGraphEdgesRequestV1(
    [property: Orleans.Id(GraphCrossPartitionFields.RequestVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.RequestTarget)] EntityRef Target,
    [property: Orleans.Id(GraphCrossPartitionFields.RequestGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.RequestLimit)] int Limit);
