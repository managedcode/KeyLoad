using System.Text;
using System.Text.Json;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;
namespace KeyLoad.Server;

internal static class ScriptDomInsertRowJson
{
    internal static string Read(string[] columns, Dom.RowValue row, ScriptDomCompositionParameters parameters, int maximumBytes, Action checkBudget, CancellationToken token)
    {
        if (row.ColumnValues.Count != columns.Length)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        using var counter = new SqlOperationByteCounter(maximumBytes, token);
        using (var writer = new Utf8JsonWriter(counter))
        {
            Write(writer, columns, row, parameters, checkBudget, token);
        }
        using var buffer = new MemoryStream(checked((int)counter.Length));
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, columns, row, parameters, checkBudget, token);
        }
        checkBudget();
        token.ThrowIfCancellationRequested();
        return Encoding.UTF8.GetString(buffer.GetBuffer(), Empty, checked((int)buffer.Length));
    }
    private static void Write(Utf8JsonWriter writer, string[] columns, Dom.RowValue row,
        ScriptDomCompositionParameters parameters, Action checkBudget, CancellationToken token)
    {
        writer.WriteStartObject();
        for (var index = Empty; index < columns.Length; index++)
        {
            checkBudget();
            token.ThrowIfCancellationRequested();
            writer.WritePropertyName(columns[index]);
            ScriptDomInsertScalar.Write(writer, row.ColumnValues[index], parameters, Empty, checkBudget, token);
        }
        writer.WriteEndObject();
    }
    private const int Empty = 0;
}
