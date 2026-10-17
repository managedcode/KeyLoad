namespace KeyLoad;

/// <summary>Requires an explicitly provisioned native ANN generation to cover an acknowledged minimum prefix.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(WaitForAnnIndexProtocol.RequestAlias)]
public sealed record WaitForAnnIndexRequest(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Collection,
    [property: Orleans.Id(2)] string VectorField,
    [property: Orleans.Id(3)] VectorSpace Space,
    [property: Orleans.Id(4)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(5)] long IndexGeneration,
    [property: Orleans.Id(6)] CommitToken MinimumToken);
