using System.Globalization;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Parses Q1 predicates and operands with the established precedence and depth budget.</summary>
internal sealed class SqlExpressionParser(SqlTokenCursor cursor, IOptions<DatabaseLimits> limitsOptions, int maximumInValues, string? alias)
{
    private const int LowestPrecedence = 0;
    private const int OrPrecedence = 1;
    private const int AndPrecedence = 2;
    private const int NotPrecedence = 3;
    private const int SinglePathSegment = 1;
    private const int FirstPathSegment = 0;

    private readonly DatabaseLimits limits = limitsOptions.Value;
    private int depth;

    internal Predicate Parse() => Expression();

    private Predicate Expression(int precedence = LowestPrecedence)
    {
        if (++depth > limits.MaxQueryDepth)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, SqlSyntax.DepthBudgetDetail);
        }
        var left = Primary();
        while ((cursor.Is(SqlSyntax.Or) ? OrPrecedence : cursor.Is(SqlSyntax.And) ? AndPrecedence : LowestPrecedence) is var priority && priority > precedence)
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
            return new Negation(Expression(NotPrecedence));
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
        if (cursor.Eat(SqlSyntax.Between))
        {
            var lower = Operand();
            cursor.Need(SqlSyntax.And);
            var upper = Operand();
            var range = new Logical(
                new Comparison(value, SqlSyntax.GreaterOrEqual, lower),
                SqlSyntax.And,
                new Comparison(value, SqlSyntax.LessOrEqual, upper));
            return negated ? new Negation(range) : range;
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
        if (values.Count > maximumInValues)
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
            return ValueOperand.Create(token.Text);
        }
        if (token.Kind == SqlTokenKind.Number)
        {
            cursor.Advance();
            if (!decimal.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                throw SqlSyntax.Invalid();
            }
            return ValueOperand.Create(number);
        }
        if (token.Kind == SqlTokenKind.Parameter)
        {
            cursor.Advance();
            return new ParameterOperand(token.Text);
        }
        if (cursor.Eat(SqlSyntax.Null))
        {
            return ValueOperand.Create(null);
        }
        if (cursor.Eat(SqlSyntax.True))
        {
            return ValueOperand.Create(true);
        }
        if (cursor.Eat(SqlSyntax.False))
        {
            return ValueOperand.Create(false);
        }
        return Field();
    }

    private FieldOperand Field()
    {
        var parts = cursor.Path();
        if (alias is not null && parts.Count > SinglePathSegment && parts[FirstPathSegment] == alias)
        {
            parts.RemoveAt(FirstPathSegment);
        }
        return new FieldOperand(parts.Count == SinglePathSegment && parts[FirstPathSegment] is SqlSyntax.MetadataId or SqlSyntax.MetadataRevision
            ? SqlSyntax.MetadataPrefix + parts[FirstPathSegment] : JsonData.Path(parts.ToArray()));
    }
}
