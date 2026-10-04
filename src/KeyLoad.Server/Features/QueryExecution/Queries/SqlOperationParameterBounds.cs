using System.Text.Json;

namespace KeyLoad.Server;

/// <summary>Rejects excessive or duplicate JSON argument structure before canonical DTO allocation.</summary>
internal sealed class SqlOperationParameterBounds(DatabaseLimits limits, CancellationToken cancellationToken)
{
    private int tokens;

    internal void Visit(JsonElement value, int depth = 1)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count(depth);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                Object(value, depth);
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                { Visit(item, depth + 1); }
                Count(depth);
                break;
            case JsonValueKind.Undefined:
                throw SqlOperationSyntax.InvalidInput();
        }
    }

    private void Object(JsonElement value, int depth)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            Count(depth);
            if (!names.Add(property.Name))
            { throw SqlOperationSyntax.InvalidInput(); }
            Visit(property.Value, depth + 1);
        }
        Count(depth);
    }

    private void Count(int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > limits.MaxJsonDepth || ++tokens > limits.MaxQueryTokens)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SqlOperationSyntax.StructureExceeded); }
    }
}
