namespace KeyLoad.Server.Features.Search;

internal static class NativeTextErrors
{
    internal static KeyLoadException Corrupt() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption,
        NativeTextProtocol.ProjectionCorrupt);

    internal static KeyLoadException Ownership() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.FormatUnsupported,
        NativeTextProtocol.ProjectionOwnership);

    internal static KeyLoadException BoundExceeded() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.BudgetExceeded,
        NativeTextProtocol.ProjectionBoundExceeded);

    internal static KeyLoadException Mismatch() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.HistoryUnavailable,
        NativeTextProtocol.ProjectionMismatch);

    internal static KeyLoadException Busy() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.BudgetExceeded,
        NativeTextProtocol.ProjectionBusy);
}
