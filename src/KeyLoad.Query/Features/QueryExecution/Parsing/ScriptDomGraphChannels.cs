using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Features.QueryExecution;
using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class ScriptDomGraphChannels
{
    internal static (string? Field, string? Text, double Weight) Text(Dom.ScalarExpression expression,
        ScriptDomGraphContext context, int depth)
    {
        if (ScriptDomGraphShape.IsNull(expression, context, depth))
        {
            return (null, null, SqlGraphSearchSyntax.DefaultWeight);
        }
        var arguments = ScriptDomGraphShape.Call(expression, TextFunction, context, depth, ChannelArity);
        return (ScriptDomGraphScalar.String(arguments[FieldIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.String(arguments[ValueIndex], context, depth + ChildDepth),
            ScriptDomGraphScalar.Weight(arguments[TextWeightIndex], context, depth + ChildDepth));
    }
    internal static (string? Field, ImmutableArray<float>? Values, VectorSpace? Space, double Weight) Vector(
        Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        if (ScriptDomGraphShape.IsNull(expression, context, depth))
        {
            return (null, null, null, SqlGraphSearchSyntax.DefaultWeight);
        }
        var arguments = ScriptDomGraphShape.Call(expression, VectorFunction, context, depth, VectorArity);
        var space = Space(arguments[SpaceIndex], context, depth + ChildDepth);
        var values = Components(arguments[ValueIndex], context, depth + ChildDepth);
        if (values.Length != space.Dimension)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.VectorSpaceDetail);
        }
        return (ScriptDomGraphScalar.String(arguments[FieldIndex], context, depth + ChildDepth), values, space,
            ScriptDomGraphScalar.Weight(arguments[VectorWeightIndex], context, depth + ChildDepth));
    }
    private static ImmutableArray<float> Components(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        if (expression is not Dom.VariableReference variable)
        {
            throw ScriptDomErrors.Unsupported();
        }
        context.Check(variable, depth);
        var value = context.Parameter(variable);
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < MinimumDimension or > SqlGraphSearchSyntax.MaximumVectorDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.ParameterDetail);
        }
        var components = ImmutableArray.CreateBuilder<float>(value.GetArrayLength());
        foreach (var component in value.EnumerateArray())
        {
            context.Budget.Check();
            if (component.ValueKind != JsonValueKind.Number || !component.TryGetSingle(out var number) || !float.IsFinite(number))
            {
                throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.VectorSpaceDetail);
            }
            components.Add(number);
        }
        return components.ToImmutable();
    }
    private static VectorSpace Space(Dom.ScalarExpression expression, ScriptDomGraphContext context, int depth)
    {
        var arguments = ScriptDomGraphShape.Call(expression, SpaceFunction, context, depth, SpaceArity);
        var id = ScriptDomGraphScalar.String(arguments[SpaceIdIndex], context, depth + ChildDepth);
        var dimension = ScriptDomGraphScalar.Integer(arguments[DimensionIndex], context, depth + ChildDepth);
        var metric = ScriptDomGraphScalar.String(arguments[MetricIndex], context, depth + ChildDepth);
        var model = ScriptDomGraphScalar.String(arguments[ModelIndex], context, depth + ChildDepth);
        var version = ScriptDomGraphScalar.String(arguments[VersionIndex], context, depth + ChildDepth);
        if (!Enum.TryParse<DistanceMetric>(metric, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed)
            || dimension is < MinimumDimension or > SqlGraphSearchSyntax.MaximumVectorDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, SqlGraphSearchSyntax.VectorSpaceDetail);
        }
        return new(id, dimension, parsed, model, version);
    }
    private const string TextFunction = "TEXT_MATCH";
    private const string VectorFunction = "VECTOR_MATCH";
    private const string SpaceFunction = "VECTOR_SPACE";
    private const int ChannelArity = 3;
    private const int VectorArity = 4;
    private const int SpaceArity = 5;
    private const int FieldIndex = 0;
    private const int ValueIndex = 1;
    private const int TextWeightIndex = 2;
    private const int SpaceIndex = 2;
    private const int VectorWeightIndex = 3;
    private const int SpaceIdIndex = 0;
    private const int DimensionIndex = 1;
    private const int MetricIndex = 2;
    private const int ModelIndex = 3;
    private const int VersionIndex = 4;
    private const int MinimumDimension = 1;
    private const int ChildDepth = 1;
}
