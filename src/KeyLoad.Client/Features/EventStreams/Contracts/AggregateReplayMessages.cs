namespace KeyLoad.Client;

internal static class AggregateReplayMessages
{
    internal const string InvalidReducer = "The reducer identity, schema versions and callback must be valid.";
    internal const string IncompleteHistory = "A snapshot-free replay must contain complete retained history.";
    internal const string InitialState = "Initial state";
    internal const string SnapshotState = "Snapshot state";
    internal const string ReducerState = "Reducer state";
    internal const string IncompatibleSnapshot = "The snapshot is incompatible with the reducer or replay head.";
    internal const string InvalidHead = "Replay stream identity, head, floor or cut is invalid.";
    internal const string UninitializedEvents = "Replay events must be an initialized immutable array.";
    internal const string DifferentGeneration = "The snapshot belongs to another stream generation.";
    internal const string EventLimitExceeded = "The replay event count exceeds the worker limit.";
    internal const string IncompleteTail = "Replay must contain the complete tail through the captured head.";
    internal const string NullEvent = "Replay contains a null event.";
    internal const string EventPayload = "Event payload";
    internal const string EventHeaders = "Event headers";
    internal const string InvalidEvent = "Replay event identity, position or ordering is invalid.";
    internal const string FutureEventSchema = "An event schema is newer than the reducer schema.";
    internal const string MissingUpcastPath = "No complete one-version upcast path reaches the reducer schema.";
    internal const string NullUpcastResult = "An event upcaster returned null.";
    internal const string UpcasterLimitExceeded = "The registered upcaster count exceeds 64.";
    internal const string InvalidUpcastTransition = "Upcasters must form unique positive one-version transitions.";
    internal const string InvalidUpcastMutation = "An event upcaster may change payload JSON and schema version only.";
    internal const string UpcastPayload = "Upcast payload";
    internal const string MissingInput = "Replay input JSON is absent.";
    internal const string InputLimitExceeded = "Combined replay state, payload and header bytes exceed the worker input limit.";
    internal const string ByteLimitSuffix = " is absent or exceeds its byte limit.";
    internal const string JsonFormatSuffix = " must be valid bounded JSON.";
}
