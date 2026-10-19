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
        if (dataType is not Dom.SqlDataTypeReference execution)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (execution.SqlDataTypeOption == Dom.SqlDataTypeOption.NVarChar
            && execution.Parameters.Count == SingleParameter && execution.Parameters[FirstParameter] is Dom.MaxLiteral)
        {
            return RelationalColumnType.Text;
        }
        if (execution.Parameters.Count != EmptyCount)
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        return execution.SqlDataTypeOption switch
        {
            Dom.SqlDataTypeOption.Bit => RelationalColumnType.Boolean,
            Dom.SqlDataTypeOption.BigInt => RelationalColumnType.WholeNumber,
            _ => throw ScriptDomOperationErrors.Unsupported()
        };
    }
}
