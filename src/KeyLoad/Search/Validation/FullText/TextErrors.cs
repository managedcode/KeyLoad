namespace KeyLoad.Server.Search;

internal static class TextErrors
{
    internal static KeyLoadException Corrupt() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption,
        TextProtocol.ProjectionCorrupt);

    internal static KeyLoadException Ownership() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.FormatUnsupported,
        TextProtocol.ProjectionOwnership);

    internal static KeyLoadException BoundExceeded() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.BudgetExceeded,
        TextProtocol.ProjectionBoundExceeded);

    internal static KeyLoadException Mismatch() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.HistoryUnavailable,
        TextProtocol.ProjectionMismatch);

    internal static KeyLoadException Busy() => KeyLoad.Errors.Fail(KeyLoad.ErrorCode.BudgetExceeded,
        TextProtocol.ProjectionBusy);
}
