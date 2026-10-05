using System.Reflection;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphIncomingMcpNativeAssertions
{
    private const string IncompleteContract = "The graph incoming native contract is incomplete.";

    internal static async Task VerifyAsync()
    {
        await VerifyTypeAsync(typeof(ReadIncomingGraphEdgesRequestV1), GraphIncomingMcpProtocol.RequestAlias,
            [nameof(ReadIncomingGraphEdgesRequestV1.Version), nameof(ReadIncomingGraphEdgesRequestV1.Target),
                nameof(ReadIncomingGraphEdgesRequestV1.Graph), nameof(ReadIncomingGraphEdgesRequestV1.Limit)]);
        await VerifyTypeAsync(typeof(GraphIncomingEdgeRowV1), GraphIncomingMcpProtocol.RowAlias,
            [nameof(GraphIncomingEdgeRowV1.Edge), nameof(GraphIncomingEdgeRowV1.DeliveredRevision)]);
        await VerifyTypeAsync(typeof(GraphIncomingEdgesPageV1), GraphIncomingMcpProtocol.PageAlias,
            [nameof(GraphIncomingEdgesPageV1.Version), nameof(GraphIncomingEdgesPageV1.Rows),
                nameof(GraphIncomingEdgesPageV1.CutPosition), nameof(GraphIncomingEdgesPageV1.Projection)]);
    }

    private static async Task VerifyTypeAsync(Type type, string alias, string[] propertyNames)
    {
        await Assert.That(type.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        var aliasAttribute = type.GetCustomAttributesData().SingleOrDefault(item =>
            item.AttributeType == typeof(global::Orleans.AliasAttribute));
        await Assert.That(aliasAttribute is not null).IsTrue();
        await Assert.That(aliasAttribute!.ConstructorArguments.Single().Value).IsEqualTo(alias);
        if (type.GetProperties().Length != propertyNames.Length)
        { throw new InvalidOperationException(IncompleteContract); }
        await VerifyFieldsAsync(type, propertyNames);
    }

    private static async Task VerifyFieldsAsync(Type type, string[] propertyNames)
    {
        for (var index = 0; index < propertyNames.Length; index++)
        {
            var property = type.GetProperty(propertyNames[index], BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            await Assert.That(property is not null).IsTrue();
            var fieldId = property!.GetCustomAttributesData().SingleOrDefault(item =>
                item.AttributeType == typeof(global::Orleans.IdAttribute));
            await Assert.That(fieldId is not null).IsTrue();
            var actual = Convert.ToUInt32(fieldId!.ConstructorArguments.Single().Value,
                System.Globalization.CultureInfo.InvariantCulture);
            await Assert.That(actual).IsEqualTo((uint)index);
        }
    }
}
