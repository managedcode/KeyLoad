namespace KeyLoad;

/// <summary>Requires execution lexical publication at an acknowledged minimum applied cut.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(WaitForIndexProtocol.RequestAlias)]
public sealed record WaitForIndexRequest(
    [property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Collection,
    [property: Orleans.Id(2)] string TextField,
    [property: Orleans.Id(3)] CommitToken MinimumToken);
