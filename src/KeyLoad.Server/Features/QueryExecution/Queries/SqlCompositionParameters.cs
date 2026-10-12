using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Binds every declared scalar once and rejects missing or unused input.</summary>
internal sealed class SqlCompositionParameters(Dictionary<string, JsonElement>? parameters, SqlOperationSyntaxReader reader)
{
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);

    internal T Read<T>()
    {
        reader.Need(SqlOperationSyntax.ParameterPrefix);
        var name = reader.Identifier();
        if (parameters is null || !parameters.Keys.Contains(name, StringComparer.Ordinal)
            || !parameters.TryGetValue(name, out var value) || !consumed.Add(name))
        { throw SqlOperationSyntax.InvalidInput(); }
        try
        {
            var result = JsonSerializer.Deserialize<T>(value, JsonDefaults.Options);
            return result is not null ? result : throw SqlOperationSyntax.InvalidInput();
        }
        catch (JsonException)
        { throw SqlOperationSyntax.InvalidInput(); }
    }

    internal void Complete()
    {
        if (parameters is null || parameters.Count != consumed.Count)
        { throw SqlOperationSyntax.InvalidInput(); }
    }
}
