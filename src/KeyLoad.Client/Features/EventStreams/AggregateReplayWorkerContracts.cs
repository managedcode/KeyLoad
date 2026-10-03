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

/// <summary>Bounds a pure client replay worker independently of server request limits.</summary>
public sealed record AggregateReplayWorkerLimits
{
    /// <summary>Maximum number of event records accepted by one worker invocation.</summary>
    public int MaximumEvents { get; init; } = 4096;

    /// <summary>Maximum UTF8 bytes in any intermediate state value.</summary>
    public int MaximumStateBytes { get; init; } = 1_048_576;

    /// <summary>Maximum combined original state, payload and header UTF8 bytes.</summary>
    public int MaximumInputBytes { get; init; } = 16_777_216;

    /// <summary>Maximum JSON nesting depth accepted for state, payloads and headers.</summary>
    public int MaximumJsonDepth { get; init; } = 64;
}
