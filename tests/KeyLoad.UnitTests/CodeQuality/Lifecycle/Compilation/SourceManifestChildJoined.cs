namespace KeyLoad.UnitTests.CodeQuality.Helpers;

internal sealed record SourceManifestChildJoined(Task Original, Task Exit, Task Output,
    Task Error, string Ready, string StandardOutput, string StandardError, bool PendingBeforeCancellation,
    bool ExitedBeforeDisposal, bool ProcessDisposed, bool OwnedRootDeleted,
    IReadOnlyList<Exception> Failures, CancellationToken Caller);
