namespace KeyLoad;

/// <summary>Requests a complete bounded replay slice under one authorized read cut.</summary>
/// <param name="Stream">The complete stream generation.</param>
/// <param name="ReducerVersion">The exact compatible worker reducer identifier.</param>
/// <param name="StateSchemaVersion">The expected positive state schema version.</param>
/// <param name="FromBeginning">Explicitly ignores the snapshot and requires full history.</param>
/// <param name="MaximumEvents">Maximum complete tail records, with no partial page.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ReadAggregateReplayRequest)]
public sealed record ReadAggregateReplayRequest([property: Orleans.Id(0)] StreamRef Stream,
    [property: Orleans.Id(1)] string ReducerVersion, [property: Orleans.Id(2)] int StateSchemaVersion = ReadAggregateReplayRequest.DefaultStateSchemaVersion,
    [property: Orleans.Id(3)] bool FromBeginning = false, [property: Orleans.Id(4)] int MaximumEvents = ReadAggregateReplayRequest.DefaultMaximumEvents)
{
    private const int DefaultStateSchemaVersion = 1;
    private const int DefaultMaximumEvents = 100;
}
