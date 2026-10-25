
namespace KeyLoad.QueryExecution;

internal static class ScriptDomErrors
{
    internal const string InvalidDetail = "The SQL syntax or query arguments are invalid.";
    internal const string UnsupportedDetail = "The SQL statement or execution clause is unsupported.";
    internal const string NodesDetail = "The SQL syntax exceeds its node budget.";
    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDetail);
}
