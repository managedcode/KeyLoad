namespace KeyLoad.Core;

internal static class StreamTraversalProtocol
{
    internal const int Version = 1;
    internal const string Purpose = "keyload-stream-traversal-v1";
    internal const int UnspecifiedBytes = 0;
    internal const int LastEventOffset = 1;
    internal const long EmptyRevision = 0L;
    internal const long FirstRevision = 1L;
    internal const string InvalidRequest = "The stream traversal request is invalid.";
    internal const string Expired = "The stream traversal cursor expired or changed scope.";
    internal const string History = "The original stream traversal history is unavailable.";
    internal const string Owner = "The stream traversal does not match this current physical owner.";
    internal const string Bytes = "The projected stream traversal exceeds its result byte budget.";
}
