using System.Reflection;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal static class GraphIncomingMcpAssertions
{
    private const string IncompleteContract = "The graph incoming execution contract is incomplete.";

    internal static async Task VerifyAsync()
    {
        await VerifyTypeAsync(typeof(ReadIncomingGraphEdgesRequest), GraphIncomingMcpProtocol.RequestAlias,
            [nameof(ReadIncomingGraphEdgesRequest.Version), nameof(ReadIncomingGraphEdgesRequest.Target),
                nameof(ReadIncomingGraphEdgesRequest.Graph), nameof(ReadIncomingGraphEdgesRequest.Limit)]);
        await VerifyTypeAsync(typeof(GraphIncomingEdgeRow), GraphIncomingMcpProtocol.RowAlias,
            [nameof(GraphIncomingEdgeRow.Edge), nameof(GraphIncomingEdgeRow.DeliveredRevision)]);
        await VerifyTypeAsync(typeof(GraphIncomingEdgesPage), GraphIncomingMcpProtocol.PageAlias,
            [nameof(GraphIncomingEdgesPage.Version), nameof(GraphIncomingEdgesPage.Rows),
                nameof(GraphIncomingEdgesPage.CutPosition), nameof(GraphIncomingEdgesPage.Projection)]);
    }

    private static async Task VerifyTypeAsync(Type type, string alias, string[] propertyNames)
    {
        await Assert.That(type.IsDefined(typeof(global::Orleans.GenerateSerializerAttribute))).IsTrue();
        var aliasAttribute = type.GetCustomAttributesData().SingleOrDefault(item =>
            item.AttributeType == typeof(global::Orleans.AliasAttribute));
        await Assert.That(aliasAttribute is not null).IsTrue();
        await Assert.That(aliasAttribute!.ConstructorArguments.Single().Value).IsEqualTo(alias);
        if (type.GetProperties().Length != propertyNames.Length)
        {
            throw new InvalidOperationException(IncompleteContract);
        }
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
