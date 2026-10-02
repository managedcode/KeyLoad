using System.Globalization;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Parses Q1 predicates and operands with the established precedence and depth budget.</summary>
internal sealed class SqlExpressionParser(SqlTokenCursor cursor, DatabaseLimits limits, string? alias)
{
    private int depth;

    internal Predicate Parse() => Expression();

    private Predicate Expression(int precedence = 0)
    {
        if (++depth > limits.MaxQueryDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlSyntax.DepthBudgetDetail);
        }
        var left = Primary();
        while ((cursor.Is(SqlSyntax.Or) ? 1 : cursor.Is(SqlSyntax.And) ? 2 : 0) is var priority && priority > precedence)
        {
            var operation = cursor.Current.Text.ToUpperInvariant();
            cursor.Advance();
            left = new Logical(left, operation, Expression(priority));
        }
        depth--;
        return left;
    }

    private Predicate Primary()
    {
        if (cursor.Eat(SqlSyntax.Not))
        {
            return new Negation(Expression(3));
        }
        if (cursor.Eat(SqlSyntax.OpenParen))
        {
            var grouped = Expression();
            cursor.Need(SqlSyntax.CloseParen);
            return grouped;
        }
        return Condition();
    }

    private Predicate Condition()
    {
        var value = Operand();
        if (cursor.Eat(SqlSyntax.Is))
        {
            var not = cursor.Eat(SqlSyntax.Not);
            var missing = cursor.Eat(SqlSyntax.Missing);
            if (!missing)
            {
                cursor.Need(SqlSyntax.Null);
            }
            return new NullTest(value, not, missing);
        }
        var negated = cursor.Eat(SqlSyntax.Not);
        if (cursor.Eat(SqlSyntax.In))
        {
            return Membership(value, negated);
        }
        if (negated || cursor.Current.Text is not (SqlSyntax.Equals or SqlSyntax.NotEquals or SqlSyntax.AlternateNotEquals or SqlSyntax.Greater or SqlSyntax.GreaterOrEqual or SqlSyntax.Less or SqlSyntax.LessOrEqual))
        {
            throw SqlSyntax.Invalid();
        }
        var operation = cursor.Current.Text;
        cursor.Advance();
        return new Comparison(value, operation, Operand());
    }

    private InPredicate Membership(Operand value, bool negated)
    {
        cursor.Need(SqlSyntax.OpenParen);
        var values = new List<Operand>();
        do
        {
            values.Add(Operand());
        } while (cursor.Eat(SqlSyntax.Comma));
        cursor.Need(SqlSyntax.CloseParen);
        if (values.Count > SqlSyntax.MaximumInValues)
        {
            throw SqlSyntax.Invalid();
        }
        return new InPredicate(value, [.. values], negated);
    }

    private Operand Operand()
    {
        var token = cursor.Current;
        if (token.Kind == SqlTokenKind.String)
        {
            cursor.Advance();
            return new ValueOperand(token.Text);
        }
        if (token.Kind == SqlTokenKind.Number)
        {
            cursor.Advance();
            if (!decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw SqlSyntax.Invalid();
            }
            return new ValueOperand(number);
        }
        if (token.Kind == SqlTokenKind.Parameter)
        {
            cursor.Advance();
            return new ParameterOperand(token.Text);
        }
        if (cursor.Eat(SqlSyntax.Null))
        {
            return new ValueOperand(null);
        }
        if (cursor.Eat(SqlSyntax.True))
        {
            return new ValueOperand(true);
        }
        if (cursor.Eat(SqlSyntax.False))
        {
            return new ValueOperand(false);
        }
        return Field();
    }

    private FieldOperand Field()
    {
        var parts = cursor.Path();
        if (alias is not null && parts.Count > 1 && parts[0] == alias)
        {
            parts.RemoveAt(0);
        }
        return new FieldOperand(parts.Count == 1 && parts[0] is SqlSyntax.MetadataId or SqlSyntax.MetadataRevision
            ? SqlSyntax.MetadataPrefix + parts[0] : JsonData.Path(parts.ToArray()));
    }
}
