namespace KeyLoad.Client;

/// <summary>Defines an exact worker reducer and its accepted state and event schema versions.</summary>
/// <param name="Version">The stable reducer identity expected by the snapshot.</param>
/// <param name="StateSchemaVersion">The positive schema version of reducer state.</param>
/// <param name="EventSchemaVersion">The positive event schema accepted by the reducer.</param>
/// <param name="InitialStateJson">Valid bounded JSON used when replay has no snapshot.</param>
/// <param name="Apply">A caller-owned pure state transition returning JSON.</param>
public sealed record AggregateReplayReducer(
    string Version,
    int StateSchemaVersion,
    int EventSchemaVersion,
    string InitialStateJson,
    Func<string, EventRecord, string> Apply);
