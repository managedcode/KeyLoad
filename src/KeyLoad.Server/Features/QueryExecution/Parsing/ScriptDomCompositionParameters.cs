using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Server;

internal sealed class ScriptDomCompositionParameters(Dictionary<string, global::System.Text.Json.JsonElement>? parameters,
    int? positionalCount, Action checkBudget, CancellationToken token)
{
    private readonly HashSet<string> used = new(StringComparer.Ordinal);
    internal T Read<T>(Dom.ScalarExpression expression)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        var name = Name(expression);
        if (parameters is null || !parameters.TryGetValue(name, out var value))
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        try
        {
            var result = global::System.Text.Json.JsonSerializer.Deserialize<T>(value, JsonDefaults.Options)
                ?? throw ScriptDomOperationErrors.Invalid();
            used.Add(name);
            checkBudget();
            token.ThrowIfCancellationRequested();
            return result;
        }
        catch (global::System.Text.Json.JsonException) { throw ScriptDomOperationErrors.Invalid(); }
    }
    internal void Complete()
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if (parameters is null || parameters.Count != used.Count)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
    }
    internal global::System.Text.Json.JsonElement ReadScalar(Dom.ScalarExpression expression)
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        var name = Name(expression);
        if (parameters is null || !parameters.TryGetValue(name, out var value))
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        used.Add(name);
        return value;
    }

    internal void CompleteOptional()
    {
        checkBudget();
        token.ThrowIfCancellationRequested();
        if ((parameters?.Count ?? EmptyCount) != used.Count)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
    }
    private const int EmptyCount = 0;

    private string Name(Dom.ScalarExpression expression)
    {
        if (expression is Dom.PrimaryExpression { Collation: not null })
        {
            throw ScriptDomOperationErrors.Unsupported();
        }
        if (positionalCount is { } count)
        {
            if (expression is Dom.MoneyLiteral money && ScriptDomSyntax.TryGetNativeSessionSlot(money, count, out var slot))
            {
                return slot;
            }
            throw ScriptDomOperationErrors.Invalid();
        }
        if (expression is Dom.VariableReference variable && variable.Name.Length > PrefixLength
            && variable.Name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return variable.Name[PrefixLength..];
        }
        throw ScriptDomOperationErrors.Unsupported();
    }
    private const int PrefixLength = 1;
    private const string Prefix = "@";
}
