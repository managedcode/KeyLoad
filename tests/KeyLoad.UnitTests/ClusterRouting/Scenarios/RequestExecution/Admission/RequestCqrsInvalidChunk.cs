namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal enum RequestCqrsInvalidChunk
{
    UnknownKind,
    WrongSequence,
    WrongEventType,
    ProgressKind,
    TerminalBeforeStarted,
    FailedWithSuccess,
    CompletedWithProblem,
    FailedWithValue,
    WrongStartedIdentity
}
