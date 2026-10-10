using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-001/003: all real mutations and recursive AST survive schema wrapping and canonical decode.</summary>
internal sealed class McpPolymorphicSchemaTests
{
    private const string Mutations = "mutations";
    private const string Query = "query";
    private const string Filter = "filter";
    private const string Left = "left";
    private const string Equal = "=";
    private const string And = "and";
    private const string UnknownKind = "unsupported-mutation";
    private const int MutationCount = 35;
    private const int QueryLimit = 10;
    private const string ComparisonKind = "comparison";
    private const string LogicalKind = "logical";
    private const string NegationKind = "not";
    private const string NullTestKind = "nullTest";
    private const string InKind = "in";
    private const string FieldKind = "field";
    private const string ValueKind = "value";
    private const string ParameterKind = "parameter";
    private const string ValueProperty = "value";
    private const string OpenArrayJson = "[1,2,3]";
    private const string OpenObjectJson = "{\"custom\":{\"enabled\":true}}";
    private static readonly ImmutableArray<string> Predicates = [ComparisonKind, LogicalKind, NegationKind, NullTestKind, InKind];
    private static readonly ImmutableArray<string> Operands = [FieldKind, ValueKind, ParameterKind];

    /// <summary>Every canonical mutation retains its discriminator schema and exact typed round trip.</summary>
    [Test]
    public async Task AcMcp001EveryCanonicalMutationIsRepresentedAndRoundTripsThroughTypedDecoder()
    {
        var descriptor = Find(McpCatalogExpectations.DocumentsCommit);
        var requestSchema = McpSchemaInspector.DefinedRequest(descriptor.InputSchema);
        var mutationSchema = requestSchema.GetProperty(McpSchemaInspector.Properties).GetProperty(Mutations)
            .GetProperty(McpSchemaInspector.Items);
        await Assert.That(McpSchemaInspector.Discriminators(mutationSchema)).IsEquivalentTo(McpMutationTestData.Discriminators);
        var request = new CommandRequest(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition, McpMutationTestData.Create());
        var publicRequest = JsonSerializer.SerializeToElement(request, JsonDefaults.Options);
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = publicRequest
        };
        var decoded = await McpNativePayloadAssertions.AssertTypedPublicPayload<CommandRequest>(publicRequest,
            descriptor.Decode(arguments).Payload);
        await Assert.That(decoded.Mutations.Length).IsEqualTo(MutationCount);
        await Assert.That(decoded.Mutations.Select(item => item.GetType()))
            .IsEquivalentTo(request.Mutations.Select(item => item.GetType()));
    }

    /// <summary>A real recursive AST retains all predicate and operand unions and every public field.</summary>
    [Test]
    public async Task AcMcp003RecursivePredicateAndOperandSchemasKeepEveryDiscriminator()
    {
        var schema = Find(McpCatalogExpectations.QueryAst).InputSchema;
        var request = McpSchemaInspector.DefinedRequest(schema);
        var filter = request.GetProperty(McpSchemaInspector.Properties).GetProperty(Query)
            .GetProperty(McpSchemaInspector.Properties).GetProperty(Filter);
        await Assert.That(McpSchemaInspector.Discriminators(filter)).IsEquivalentTo(Predicates);
        var comparison = filter.GetProperty(McpSchemaInspector.AnyOf)[0];
        var operand = comparison.GetProperty(McpSchemaInspector.Properties).GetProperty(Left);
        await Assert.That(McpSchemaInspector.Discriminators(operand)).IsEquivalentTo(Operands);
        await Assert.That(McpSchemaInspector.References(schema).IsEmpty).IsFalse();
        var typed = new AstQueryRequest(McpCanonicalTestData.Partition, new SelectQuery(McpCanonicalTestData.Resource, null,
            [new Selection(McpCanonicalTestData.Field, McpCanonicalTestData.Field)],
            new Logical(new Comparison(new FieldOperand(McpCanonicalTestData.Field), Equal, new ParameterOperand(McpCanonicalTestData.Field)),
                And, new Negation(new NullTest(new FieldOperand(McpCanonicalTestData.Field), false, false))), [], QueryLimit));
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(typed, JsonDefaults.Options)
        };
        var payload = Find(McpCatalogExpectations.QueryAst).Decode(arguments).Payload;
        var decoded = await McpNativePayloadAssertions.AssertTypedPublicPayload<AstQueryRequest>(arguments[
            McpCanonicalTestData.RequestKey], payload);
        await Assert.That(decoded.Query.Filter is Logical
        { Right: Negation { Inner: NullTest } }).IsTrue();
    }

    /// <summary>An unsupported discriminator fails canonical decode with safe validation.</summary>
    [Test]
    public async Task AcMcp003UnknownMutationDiscriminatorIsAValidationFailure()
    {
        var request = new CommandRequest(McpCanonicalTestData.StableId, McpCanonicalTestData.Partition, McpMutationTestData.Create());
        var node = JsonSerializer.SerializeToNode(request, JsonDefaults.Options)!.AsObject();
        node[Mutations]![0]![McpSchemaInspector.Kind] = UnknownKind;
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(node)
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => Find(McpCatalogExpectations.DocumentsCommit).Decode(arguments)).Code)
            .IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-MCP-003: arbitrary JSON operands stay open and decode as scalar, array, or object values.</summary>
    [Test]
    public async Task AcMcp003ValueOperandSchemaRemainsOpenForArbitraryJson()
    {
        var schema = FindValueOperandSchema(Find(McpCatalogExpectations.QueryAst).InputSchema);
        await Assert.That(IsUnconstrainedJsonSchema(schema)).IsTrue();

        var values = new[]
        {
            JsonSerializer.SerializeToElement("user-scalar", JsonDefaults.Options),
            JsonSerializer.Deserialize<JsonElement>(OpenArrayJson),
            JsonSerializer.Deserialize<JsonElement>(OpenObjectJson)
        };
        var expectedKinds = new[] { JsonValueKind.String, JsonValueKind.Array, JsonValueKind.Object };
        for (var index = 0; index < values.Length; index++)
        {
            var request = McpCanonicalTestData.Ast() with
            {
                Query = McpCanonicalTestData.Ast().Query with
                {
                    Filter = new Comparison(new FieldOperand(McpCanonicalTestData.Field), Equal,
                        new ValueOperand(values[index]))
                }
            };
            var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [McpCanonicalTestData.RequestKey] = JsonSerializer.SerializeToElement(request, JsonDefaults.Options)
            };
            var payload = Find(McpCatalogExpectations.QueryAst).Decode(arguments).Payload;
            var decoded = NativeSerialization.Deserialize<AstQueryRequest>(payload.Span);
            var actualValue = ((Comparison)decoded.Query.Filter!).Right is ValueOperand valueOperand
                ? valueOperand.Value : throw new InvalidOperationException(ValueKind);
            await Assert.That(actualValue.ValueKind).IsEqualTo(expectedKinds[index]);
            await Assert.That(actualValue.GetRawText()).IsEqualTo(values[index].GetRawText());
        }
    }

    private static JsonElement FindValueOperandSchema(JsonElement root)
    {
        foreach (var pointer in McpSchemaInspector.References(root))
        {
            var found = FindValueOperandSchemaNode(McpSchemaInspector.Resolve(root, pointer));
            if (found.ValueKind != JsonValueKind.Undefined)
            { return found; }
        }
        throw new InvalidOperationException(ValueKind);
    }

    private static JsonElement FindValueOperandSchemaNode(JsonElement node)
    {
        return node.ValueKind switch
        {
            JsonValueKind.Object => FindObjectValueOperandSchemaNode(node),
            JsonValueKind.Array => FindArrayValueOperandSchemaNode(node),
            _ => default
        };
    }

    private static JsonElement FindObjectValueOperandSchemaNode(JsonElement node)
    {
        if (node.TryGetProperty(McpSchemaInspector.Properties, out var properties) &&
            properties.TryGetProperty(McpSchemaInspector.Kind, out var kind) &&
            kind.TryGetProperty(McpSchemaInspector.Const, out var discriminator) && discriminator.GetString() == ValueKind)
        { return properties.GetProperty(ValueProperty); }

        foreach (var property in node.EnumerateObject())
        {
            var found = FindValueOperandSchemaNode(property.Value);
            if (found.ValueKind != JsonValueKind.Undefined)
            { return found; }
        }
        return default;
    }

    private static JsonElement FindArrayValueOperandSchemaNode(JsonElement node)
    {
        foreach (var item in node.EnumerateArray())
        {
            var found = FindValueOperandSchemaNode(item);
            if (found.ValueKind != JsonValueKind.Undefined)
            { return found; }
        }
        return default;
    }

    private static bool IsUnconstrainedJsonSchema(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.True)
        { return true; }
        if (schema.ValueKind != JsonValueKind.Object)
        { return false; }
        var constraintKeywords = new[]
        {
            McpSchemaInspector.Ref, McpSchemaInspector.Type, McpSchemaInspector.Const, McpSchemaInspector.Enum,
            McpSchemaInspector.Properties, McpSchemaInspector.Items, McpSchemaInspector.AnyOf,
            McpSchemaInspector.OneOf, "allOf", "not", "required", "pattern", "format", "minimum", "maximum",
            "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "minItems", "maxItems",
            "minProperties", "maxProperties", "uniqueItems", "additionalProperties", "unevaluatedProperties",
            "unevaluatedItems", "prefixItems", "contains", "minContains", "maxContains", "propertyNames",
            "patternProperties", "dependentRequired", "dependentSchemas", "if", "then", "else", "contentSchema"
        };
        return !schema.EnumerateObject().Any(property => constraintKeywords.Contains(property.Name, StringComparer.Ordinal));
    }

    private static McpOperationDescriptor Find(string name)
    {
        if (!McpOperationCatalog.TryGet(name, out var descriptor))
        { throw new InvalidOperationException(name); }
        return descriptor!;
    }
}
