namespace KeyLoad.UnitTests.Features.ClientApi;

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
