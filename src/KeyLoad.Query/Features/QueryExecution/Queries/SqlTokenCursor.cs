using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Owns one SQL token position shared by projection and expression parsing.</summary>
internal sealed class SqlTokenCursor(List<SqlToken> tokens)
{
    private int offset;

    internal SqlToken Current => tokens[offset];

    internal bool Is(string text) => !Current.Quoted && string.Equals(Current.Text, text, StringComparison.OrdinalIgnoreCase);

    internal bool Eat(string text)
    {
        if (!Is(text))
        {
            return false;
        }
        offset++;
        return true;
    }

    internal void Need(string text)
    {
        if (!Eat(text))
        {
            throw SqlSyntax.Invalid();
        }
    }

    internal void Advance() => offset++;

    internal string Identifier()
    {
        if (Current.Kind != SqlTokenKind.Identifier)
        {
            throw SqlSyntax.Invalid();
        }
        var value = Current.Text;
        offset++;
        JsonData.Identifier(value);
        return value;
    }

    internal List<string> Path()
    {
        var parts = new List<string> { Identifier() };
        while (Eat(SqlSyntax.Dot))
        {
            parts.Add(Identifier());
        }
        return parts;
    }
}
