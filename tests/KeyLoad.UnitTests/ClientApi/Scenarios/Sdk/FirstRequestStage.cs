namespace KeyLoad.UnitTests.ClientApi;

internal enum FirstRequestStage
{
    WaitingForHandler,
    HandlerEntered,
    WritingFirstChunk,
    FirstChunkWritten,
    FlushingFirstChunk,
    FirstChunkFlushed,
    WaitingForSecondWriteRelease,
    WritingSecondChunk,
    SecondChunkWritten,
    FlushingSecondChunk,
    SecondChunkFlushed,
    WaitingForRequestAborted,
    RequestAborted
}
