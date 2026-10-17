namespace KeyLoad.Client;

internal static class AggregateReplayProtocol
{
    internal const int InvalidSchemaVersion = 0;
    internal const int MissingGeneration = 0;
    internal const int FirstRetainedRevision = 1;
    internal const int NoRevision = 0;
    internal const int RevisionStep = 1;
    internal const int NoPosition = 0;
    internal const int NoSequence = 0;
    internal const int FirstEventIndex = 0;
    internal const int SchemaVersionStep = 1;
}
