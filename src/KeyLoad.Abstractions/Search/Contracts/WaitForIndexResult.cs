namespace KeyLoad;

/// <summary>Reports the applied cut whose eligible native lexical generation completed publication.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(WaitForIndexProtocol.ResultAlias)]
public sealed record WaitForIndexResult(
    [property: Orleans.Id(0)] CommitToken AppliedToken,
    [property: Orleans.Id(1)] long SchemaVersion,
    [property: Orleans.Id(2)] long PolicyEpoch);
