namespace KeyLoad.Server;

internal static class ScriptDomContextIdentity
{
    private const int SingleCommand = 1;
    private const int FirstCommand = 0;
    private const int PrefixLength = 2;
    private const int HexGuidLength = 32;
    private const string BinaryPrefix = "0x";
    private const string GuidFormat = "N";

    internal static Guid Read(global::Microsoft.SqlServer.TransactSql.ScriptDom.SetCommandStatement statement,
        Action checkBudget, CancellationToken token)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (statement.Commands.Count != SingleCommand
            || statement.Commands[FirstCommand] is not global::Microsoft.SqlServer.TransactSql.ScriptDom.GeneralSetCommand
            {
                CommandType: global::Microsoft.SqlServer.TransactSql.ScriptDom.GeneralSetCommandType.ContextInfo,
                Parameter: global::Microsoft.SqlServer.TransactSql.ScriptDom.BinaryLiteral value
            })
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (value.Collation is not null)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (value.Value.Length != PrefixLength + HexGuidLength
            || !value.Value.StartsWith(BinaryPrefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(value.Value.AsSpan(PrefixLength), GuidFormat, out var identity)
            || identity == Guid.Empty)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        checkBudget();
        token.ThrowIfCancellationRequested();
        return identity;
    }
}
