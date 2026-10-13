using System.Text.Json;

namespace KeyLoad.Server;

internal sealed record PostgresPreparedStatement(string Sql, int[] ParameterOids, bool Rows, int RetainedBytes);

internal sealed record PostgresBoundPortal(PostgresPreparedStatement Statement,
    Dictionary<string, JsonElement> Parameters, short ResultFormat, int RetainedBytes)
{
    internal bool Completed { get; set; }
}
