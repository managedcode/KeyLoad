namespace KeyLoad.Server;

internal static class ScriptDomOperationErrors
{
    private const string InvalidDetail = "The unified SQL statement or bound arguments are invalid.";
    private const string UnsupportedDetail = "The unified SQL version, statement or target operation is unsupported.";

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDetail);
}
