using System.Text.Json;
using KeyLoad.UnitTests.Features.ClientApi;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Independent SQL spelling and boundary recipes for the accepted comment grammar.</summary>
internal static class SqlOperationCommentTestData
{
    internal const string Block = "/* outer /* nested */ body */";
    internal const string CallStart = "/* lead */ EXEC /* root */ ";
    internal const string CallEnd = " /* target */ @arguments /* parameter */ ; /* end */";
    internal const string EndOfFileLine = " -- trailing private-input-canary";
    internal const string LineBreak = "\r\n";
    internal const string CrLfCall = "-- leading\r\nEXEC-- root\r\nkeyload_query_capabilities-- target\r\n@arguments-- parameter\r\n;-- end";
    internal const string UnicodeBlock = "/* é 漢 🙂 */ ";
    internal const string Select = "/* lead /* nested */ */ SELECT status /* -- intact inside a comment */ FROM orders WHERE status = @status";
    internal const string Top = "-- leading\r\nSELECT /* root */ TOP (1) * FROM orders WHERE status = @status";
    internal const string Document = "{\"text\":\"/* document */ -- intact\"}";
    internal const string OpenBlock = "/*";
    internal const string CloseBlock = "*/";
    internal const string NestedTail = "/* nested */ */ ";
    internal const string Space = " ";
    internal const string Semicolon = ";";
    internal const string WrongCaseParameter = "ARGUMENTS";
    internal const string DuplicateArguments = "{\"request\":false,\"request\":true}";
    internal const int ChunkCharacters = 256;
    internal const int PairCharacters = 2;
    internal const int BoundaryStep = 1;
    internal const int CallTokens = 3;
    internal const int ShallowDepth = 2;
    internal const int ExcessDepth = ShallowDepth + BoundaryStep;
    internal const int LongCommentChunks = 8;
    internal const char Padding = 'x';
    internal const char Whitespace = ' ';

    internal static string Call(string name) => CallStart + name + CallEnd;

    internal static SqlOperationRequest Request(string sql) => SqlOperationTestData.Request(sql,
        JsonSerializer.Deserialize<JsonElement>(SqlOperationTestData.EmptyObject));

    internal static string PlainCall => SqlOperationTestData.CallPrefix + McpCatalogExpectations.QueryCapabilities
        + SqlOperationTestData.CallSuffix;

    internal static string Nested(int depth) => string.Concat(Enumerable.Repeat(OpenBlock, depth))
        + SqlOperationTestData.PrivateMarker + string.Concat(Enumerable.Repeat(CloseBlock, depth));

    internal static IEnumerable<string> BoundaryPrefixes()
    {
        yield return new string(Whitespace, ChunkCharacters - BoundaryStep) + Block + Space;
        yield return OpenBlock + new string(Padding, ChunkCharacters - PairCharacters - BoundaryStep) + CloseBlock + Space;
        yield return OpenBlock + new string(Padding, ChunkCharacters - PairCharacters - BoundaryStep) + NestedTail;
        yield return new string(Whitespace, ChunkCharacters - PairCharacters) + EndOfFileLine + LineBreak;
    }
}
