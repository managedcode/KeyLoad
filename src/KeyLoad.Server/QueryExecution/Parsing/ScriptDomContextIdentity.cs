namespace KeyLoad.Server;

internal static class ScriptDomContextIdentity
{
    private const int SingleCommand = 1;
    private const int FirstCommand = 0;
    private const int PrefixLength = 2;
    private const int HexGuidLength = 32;
    private const int HexEpochLength = 16;
    private const string BinaryPrefix = "0x";
    private const string GuidFormat = "N";
    internal static Guid Read(global::Microsoft.SqlServer.TransactSql.ScriptDom.SetCommandStatement statement,
        Action checkBudget, CancellationToken token)
        => ReadControl(statement, checkBudget, token).Identity;
    internal static (Guid Identity, long? OwnershipEpoch) ReadControl(global::Microsoft.SqlServer.TransactSql.ScriptDom.SetCommandStatement statement,
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
            && value.Value.Length != PrefixLength + HexGuidLength + HexEpochLength)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        if (!value.Value.StartsWith(BinaryPrefix, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(value.Value.AsSpan(PrefixLength, HexGuidLength), GuidFormat, out var identity)
            || identity == Guid.Empty)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        long? ownershipEpoch = null;
        if (value.Value.Length == PrefixLength + HexGuidLength + HexEpochLength)
        {
            if (!long.TryParse(value.Value.AsSpan(PrefixLength + HexGuidLength),
                global::System.Globalization.NumberStyles.AllowHexSpecifier,
                global::System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                || parsed < SqlMutationIdentityFields.InitialOwnershipEpoch)
            {
                throw ScriptDomOperationErrors.Invalid();
            }
            ownershipEpoch = parsed;
        }
        checkBudget();
        token.ThrowIfCancellationRequested();
        return (identity, ownershipEpoch);
    }
}

