namespace KeyLoad.Server;

/// <summary>Stable procedural SQL tokens and safe diagnostics, independent of target execution.</summary>
internal static class SqlOperationSyntax
{
    internal const string Select = "SELECT";
    internal const string Explain = "EXPLAIN";
    internal const string Call = "CALL";
    internal const string Invalid = "The unified SQL statement or bound arguments are invalid.";
    internal const string Unsupported = "The unified SQL version, statement or target operation is unsupported.";
    internal const string TextExceeded = "The unified SQL text exceeds its byte budget.";
    internal const string StructureExceeded = "The unified SQL parameters or syntax exceed their structural budget.";
    internal const string SequentialOnly = "The SQL byte counter supports sequential writes only.";
    internal const char ParameterPrefix = '@';
    internal const char OpenParenthesis = '(';
    internal const char CloseParenthesis = ')';
    internal const char Terminator = ';';
    internal const char Underscore = '_';
    internal const char FirstUpper = 'A';
    internal const char LastUpper = 'Z';
    internal const char FirstLower = 'a';
    internal const char LastLower = 'z';
    internal const char FirstDigit = '0';
    internal const char LastDigit = '9';
    internal const int CallParameterCount = 1;

    internal static KeyLoadException InvalidInput() => Errors.Fail(ErrorCode.Validation, Invalid);
    internal static KeyLoadException UnsupportedInput() => Errors.Fail(ErrorCode.UnsupportedCapability, Unsupported);
}
