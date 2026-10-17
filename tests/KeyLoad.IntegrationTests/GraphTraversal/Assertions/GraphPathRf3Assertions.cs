using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal static class GraphPathRf3Assertions
{
    internal static async Task AssertPathAsync(GraphShortestPathResult result, EntityRef[] vertices,
        string[] edgeIds, string label, long minimumCutPosition = 1L)
    {
        await Assert.That(result.Version).IsEqualTo(1);
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Hops).IsEqualTo(edgeIds.Length);
        await Assert.That(result.Vertices.SequenceEqual(vertices)).IsTrue();
        await Assert.That(result.Edges.Select(edge => edge.Id).SequenceEqual(edgeIds)).IsTrue();
        await Assert.That(result.Edges.Select(edge => edge.Label).SequenceEqual(
            Enumerable.Repeat(label, edgeIds.Length))).IsTrue();
        for (var index = 0; index < result.Edges.Length; index++)
        {
            await Assert.That(result.Edges[index].From).IsEqualTo(vertices[index]);
            await Assert.That(result.Edges[index].To).IsEqualTo(vertices[index + 1]);
        }
        await Assert.That(result.CutPosition).IsGreaterThanOrEqualTo(minimumCutPosition);
    }

    internal static async Task AssertEquivalentAsync(GraphShortestPathResult expected,
        GraphShortestPathResult actual)
    {
        await Assert.That(actual.Version).IsEqualTo(expected.Version);
        await Assert.That(actual.Found).IsEqualTo(expected.Found);
        await Assert.That(actual.Hops).IsEqualTo(expected.Hops);
        await Assert.That(actual.Vertices.SequenceEqual(expected.Vertices)).IsTrue();
        await Assert.That(actual.Edges.SequenceEqual(expected.Edges)).IsTrue();
        await Assert.That(expected.CutPosition).IsGreaterThan(0L);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
    }

    internal static async Task AssertSdkErrorAsync<T>(Result<T> result, ErrorCode expected)
    {
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(expected.ToString());
    }

    internal static Task<Guid?> AssertMcpErrorAsync(CallToolResult result, ErrorCode expected)
        => McpCallerAssertions.ErrorAsync(result, expected, dispatched: true);

    internal static async Task AssertNoPathAsync(GraphShortestPathResult result)
    {
        await Assert.That(result.Version).IsEqualTo(1);
        await Assert.That(result.Found).IsFalse();
        await Assert.That(result.Hops).IsNull();
        await Assert.That(result.Vertices).IsEmpty();
        await Assert.That(result.Edges).IsEmpty();
        await Assert.That(result.CutPosition).IsGreaterThan(0L);
    }

    internal static async Task AssertProjectedAsync(GraphShortestPathResult result)
    {
        var attributes = result.Edges.Single(edge => edge.Id == "a-end-a").AttributesJson;
        await Assert.That(attributes.Contains(GraphPathRf3Scenario.SecretMarker, StringComparison.Ordinal)).IsFalse();
        await Assert.That(attributes.Contains("visible", StringComparison.Ordinal)).IsTrue();
    }
}
