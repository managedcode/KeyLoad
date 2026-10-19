namespace KeyLoad;

/// <summary>Contains one canonical source edge validated against a delivered revision.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.IncomingRow)]
public sealed record GraphIncomingEdgeRow(
    [property: Orleans.Id(GraphCrossPartitionFields.RowEdge)] EdgeRecord Edge,
    [property: Orleans.Id(GraphCrossPartitionFields.DeliveredRevision)] long DeliveredRevision);
