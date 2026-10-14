using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomTableScalarType
{
    private const int EmptyCount = 0;
    private const int SingleParameter = 1;
    private const int FirstParameter = 0;
    internal static RelationalColumnType Read(Dom.DataTypeReference dataType, ScriptDomTableLoweringContext context)
    {
        context.Check();
        if (dataType is not Dom.SqlDataTypeReference native)
        { throw ScriptDomOperationErrors.Unsupported(); }
        if (native.SqlDataTypeOption == Dom.SqlDataTypeOption.NVarChar
            && native.Parameters.Count == SingleParameter && native.Parameters[FirstParameter] is Dom.MaxLiteral)
        { return RelationalColumnType.Text; }
        if (native.Parameters.Count != EmptyCount)
        { throw ScriptDomOperationErrors.Unsupported(); }
        return native.SqlDataTypeOption switch
        {
            Dom.SqlDataTypeOption.Bit => RelationalColumnType.Boolean,
            Dom.SqlDataTypeOption.BigInt => RelationalColumnType.WholeNumber,
            _ => throw ScriptDomOperationErrors.Unsupported()
        };
    }
}
