using System.Text.Json;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphCrossPartitionAssertions
{
    internal static async Task RoundTripsAllFields<T>(T expected)
    {
        var payload = OrleansSerialization.Serialize(expected);
        var actual = OrleansSerialization.Deserialize<T>(payload);
        var expectedJson = JsonSerializer.SerializeToUtf8Bytes(expected, JsonDefaults.Options);
        var actualJson = JsonSerializer.SerializeToUtf8Bytes(actual, JsonDefaults.Options);
        await Assert.That(actualJson.SequenceEqual(expectedJson)).IsTrue();
    }
}
