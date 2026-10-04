namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Stable safe diagnostics for SQL comment lexical failures.</summary>
internal static class SqlCommentSyntax
{
    internal const string UnterminatedBlockDetail = "The SQL block comment is unterminated.";
    internal const string BlockDepthDetail = "The SQL comment depth budget is exceeded.";
}
