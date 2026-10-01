using System.Globalization;
using System.Text;
using KeyLoad.Core;

namespace KeyLoad.Query;

/// <summary>A bounded Q1 parser. Unsupported syntax is rejected, never passed through to storage.</summary>
public sealed class SqlParser
{
    private readonly List<Token> tokens;
    private readonly DatabaseLimits limits;
    private int offset;
    private int depth;
    private string? alias;
    private enum Kind { Identifier, String, Number, Parameter, Symbol, End }
    private sealed record Token(Kind Kind, string Text, bool Quoted = false);
    public SqlParser(string sql, DatabaseLimits limits)
    {
        this.limits = limits;
        if (Encoding.UTF8.GetByteCount(sql) > limits.MaxQueryBytes) throw Errors.Fail(ErrorCode.BudgetExceeded, "The SQL byte budget is exceeded.");
        tokens = Lex(sql, limits.MaxQueryTokens);
    }
    public SelectQuery Parse()
    {
        var explain = Eat("EXPLAIN");
        Need("SELECT");
        var projection = new List<Selection>();
        if (Eat("*")) projection.Add(new("*", "*"));
        else do
        {
            var path = Path();
            var output = Eat("AS") ? Identifier() : path.Last();
            projection.Add(new(JsonData.Path(path.ToArray()), output));
        } while (Eat(","));
        Need("FROM");
        var collection = Identifier();
        if (Eat("AS")) alias = Identifier();
        else if (Current.Kind == Kind.Identifier && !Is("WHERE") && !Is("ORDER") && !Is("LIMIT")) alias = Identifier();
        string BoundPath(string path)
        {
            if (path == "*") return path;
            var parts = JsonData.PathSegments(path).ToList();
            if (alias is not null && parts.Count > 1 && parts[0] == alias) parts.RemoveAt(0);
            return parts.Count == 1 && parts[0] is "id" or "revision" ? "/@" + parts[0] : JsonData.Path(parts.ToArray());
        }
        projection = projection.Select(p => p with { Path = BoundPath(p.Path) }).ToList();
        Predicate? filter = Eat("WHERE") ? Expression() : null;
        var order = new List<Ordering>();
        if (Eat("ORDER"))
        {
            Need("BY");
            do { var field = BoundPath(JsonData.Path(Path().ToArray())); var descending = Eat("DESC"); if (!descending) Eat("ASC"); order.Add(new(field, descending)); } while (Eat(","));
        }
        var limit = 100;
        if (Eat("LIMIT"))
        {
            if (Current.Kind != Kind.Number || !int.TryParse(Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out limit)) throw Syntax();
            offset++;
        }
        Eat(";");
        if (Current.Kind != Kind.End) throw Errors.Fail(ErrorCode.UnsupportedCapability, "The SQL statement contains unsupported syntax.");
        if (limit < 1 || limit > limits.MaxResults || projection.Count > 256 || order.Count > 16
            || projection.Select(p => p.Alias).Distinct(StringComparer.Ordinal).Count() != projection.Count) throw Syntax();
        return new(collection, alias, projection.ToArray(), filter, order.ToArray(), limit, explain);
    }
    private Predicate Expression(int precedence = 0)
    {
        if (++depth > limits.MaxQueryDepth) throw Errors.Fail(ErrorCode.BudgetExceeded, "The SQL depth budget is exceeded.");
        Predicate left;
        if (Eat("NOT")) left = new Negation(Expression(3));
        else if (Eat("(")) { left = Expression(); Need(")"); }
        else
        {
            var value = Operand();
            if (Eat("IS")) { var not = Eat("NOT"); var missing = Eat("MISSING"); if (!missing) Need("NULL"); left = new NullTest(value, not, missing); }
            else
            {
                var not = Eat("NOT");
                if (Eat("IN"))
                {
                    Need("("); var values = new List<Operand>(); do { values.Add(Operand()); } while (Eat(",")); Need(")");
                    if (values.Count > 256) throw Syntax(); left = new InPredicate(value, values.ToArray(), not);
                }
                else
                {
                    if (not || Current.Text is not ("=" or "!=" or "<>" or ">" or ">=" or "<" or "<=")) throw Syntax();
                    var operation = Current.Text; offset++; left = new Comparison(value, operation, Operand());
                }
            }
        }
        while ((Is("OR") ? 1 : Is("AND") ? 2 : 0) is var priority && priority > precedence)
        {
            var operation = Current.Text.ToUpperInvariant(); offset++;
            left = new Logical(left, operation, Expression(priority));
        }
        depth--;
        return left;
    }
    private Operand Operand()
    {
        var token = Current;
        if (token.Kind == Kind.String) { offset++; return new ValueOperand(token.Text); }
        if (token.Kind == Kind.Number)
        {
            offset++; if (!decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw Syntax();
            return new ValueOperand(value);
        }
        if (token.Kind == Kind.Parameter) { offset++; return new ParameterOperand(token.Text); }
        if (Eat("NULL")) return new ValueOperand(null);
        if (Eat("TRUE")) return new ValueOperand(true);
        if (Eat("FALSE")) return new ValueOperand(false);
        var parts = Path();
        if (alias is not null && parts.Count > 1 && parts[0] == alias) parts.RemoveAt(0);
        return new FieldOperand(parts.Count == 1 && parts[0] is "id" or "revision" ? "/@" + parts[0] : JsonData.Path(parts.ToArray()));
    }
    private List<string> Path() { var parts = new List<string> { Identifier() }; while (Eat(".")) parts.Add(Identifier()); return parts; }
    private string Identifier() { if (Current.Kind != Kind.Identifier) throw Syntax(); var value = Current.Text; offset++; JsonData.Identifier(value); return value; }
    private Token Current => tokens[offset];
    private bool Is(string text) => !Current.Quoted && string.Equals(Current.Text, text, StringComparison.OrdinalIgnoreCase);
    private bool Eat(string text) { if (!Is(text)) return false; offset++; return true; }
    private void Need(string text) { if (!Eat(text)) throw Syntax(); }
    private static KeyLoadException Syntax() => Errors.Fail(ErrorCode.Validation, "The SQL statement is invalid for the supported Q1 dialect.");
    private static List<Token> Lex(string sql, int maximum)
    {
        var result = new List<Token>();
        for (var i = 0; i < sql.Length;)
        {
            var c = sql[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (result.Count >= maximum) throw Errors.Fail(ErrorCode.BudgetExceeded, "The SQL token budget is exceeded.");
            if (c is '\'' or '"')
            {
                var quote = c; var value = new StringBuilder(); var closed = false; i++;
                while (i < sql.Length)
                {
                    if (sql[i] == quote)
                    {
                        i++; if (i < sql.Length && sql[i] == quote) { value.Append(quote); i++; }
                        else { closed = true; break; }
                    }
                    else value.Append(sql[i++]);
                }
                if (!closed) throw Syntax();
                result.Add(new(quote == '\'' ? Kind.String : Kind.Identifier, value.ToString(), quote == '"'));
            }
            else if (char.IsLetter(c) || c is '_' or '@')
            {
                var start = i++; while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                result.Add(new(c == '@' ? Kind.Parameter : Kind.Identifier, sql[(c == '@' ? start + 1 : start)..i]));
            }
            else if (char.IsDigit(c) || c == '-' && i + 1 < sql.Length && char.IsDigit(sql[i + 1]))
            {
                var start = i++; while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] is '.' or 'e' or 'E' or '+' or '-')) i++;
                result.Add(new(Kind.Number, sql[start..i]));
            }
            else
            {
                var text = c.ToString(); i++;
                if (i < sql.Length && (c is '>' or '<' or '!' && sql[i] == '=' || c == '<' && sql[i] == '>')) text += sql[i++];
                if (text is not ("(" or ")" or "," or "." or "*" or ";" or "=" or "!=" or "<>" or ">" or ">=" or "<" or "<=")) throw Syntax();
                result.Add(new(Kind.Symbol, text));
            }
        }
        result.Add(new(Kind.End, ""));
        return result;
    }
}
