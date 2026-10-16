using KeyLoad.Core.Features.RelationalStorage;

namespace KeyLoad.Query;

/// <summary>Binds existing scalar predicates to the current authorized typed collection before candidate reads.</summary>
internal static class RelationalQueryBinding
{
    internal const string InvalidDetail = "The typed query binding is invalid.";

    internal static void Bind(ResourceDefinition resource, AstQueryRequest request)
    {
        if (resource.RelationalSchema is null)
        {
            return;
        }
        var columns = RelationalSchemaRules.Columns(resource);
        var operands = new RelationalQueryOperandBinding(columns, request.Parameters);
        foreach (var path in request.Query.Order.Select(order => order.Path))
        {
            _ = operands.Field(path);
        }
        Visit(request.Query.Filter, operands);
    }

    private static void Visit(Predicate? predicate, RelationalQueryOperandBinding operands)
    {
        switch (predicate)
        {
            case Comparison comparison:
                operands.Compare(comparison.Left, comparison.Right);
                break;
            case Logical logical:
                Visit(logical.Left, operands);
                Visit(logical.Right, operands);
                break;
            case Negation negation:
                Visit(negation.Inner, operands);
                break;
            case NullTest test:
                _ = operands.Infer(test.Value);
                break;
            case InPredicate list:
                var domain = operands.Infer(list.Value);
                foreach (var item in list.Values)
                {
                    RelationalQueryOperandBinding.RequireComparable(domain, operands.Infer(item));
                }
                break;
        }
    }
}
