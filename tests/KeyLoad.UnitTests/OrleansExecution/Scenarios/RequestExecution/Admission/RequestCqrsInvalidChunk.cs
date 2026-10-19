namespace KeyLoad.UnitTests.Features.OrleansExecution;

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
