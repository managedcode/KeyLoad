namespace KeyLoad.Core.Features.Messaging;

internal static class QueueDispatchStateUpdates
{
    private const int SchemaVersionStep = 1;
    private const int MinimumSchemaVersion = 1;
    private const string InvalidExpectedVersion = "The expected queue schema version must be positive.";
    private const string MissingResource = "The queue does not exist at the expected schema version.";
    private const string WrongKind = "Dispatch control requires an existing work queue.";
    private const string StaleVersion = "The expected queue schema version is stale.";
    private const string NoChange = "Dispatch control must change the queue pause state.";
    private const string VersionExhausted = "The queue schema version cannot be incremented.";

    internal static ResourceDefinition Apply(ResourceDefinition? current, long expectedSchemaVersion, bool paused)
    {
        if (expectedSchemaVersion < MinimumSchemaVersion)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidExpectedVersion);
        }
        if (current is null)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, MissingResource);
        }
        if (current.Kind != ResourceKind.WorkQueue)
        {
            throw Errors.Fail(ErrorCode.Validation, WrongKind);
        }
        if (current.SchemaVersion != expectedSchemaVersion)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, StaleVersion);
        }
        if (current.Paused == paused)
        {
            throw Errors.Fail(ErrorCode.Validation, NoChange);
        }
        if (current.SchemaVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, VersionExhausted);
        }
        return current with
        {
            Paused = paused,
            SchemaVersion = current.SchemaVersion + SchemaVersionStep
        };
    }
}
