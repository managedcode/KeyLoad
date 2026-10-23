
namespace KeyLoad.Features.QueryExecution;

internal static class ScriptDomErrors
{
    internal const string InvalidDetail = "The SQL syntax or query arguments are invalid.";
    internal const string UnsupportedDetail = "The SQL statement or execution clause is unsupported.";
    internal const string BytesDetail = "The SQL text exceeds its byte budget.";
    internal const string TokensDetail = "The SQL syntax exceeds its token budget.";
    internal const string DepthDetail = "The SQL syntax exceeds its depth budget.";
    internal const string NodesDetail = "The SQL syntax exceeds its node budget.";
    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
    internal static KeyLoadException Unsupported() => Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedDetail);
}
