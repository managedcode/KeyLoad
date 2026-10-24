using System.Text.Json;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class MetamorphicReferenceAssertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task PageAsync(QueryPage actual, QueryRow[] expected, long cut, string path, bool final)
    {
        await Assert.That(actual.CutPosition).IsEqualTo(cut);
        await Assert.That(actual.AccessPath).IsEqualTo(path);
        await Assert.That(actual.Cursor is null).IsEqualTo(final);
        await Assert.That(actual.Rows.Length).IsEqualTo(expected.Length);
        foreach (var (row, golden) in actual.Rows.Zip(expected))
        {
            using var value = JsonDocument.Parse(row.Json);
            using var wanted = JsonDocument.Parse(golden.Json);
            await Assert.That(JsonElement.DeepEquals(value.RootElement, wanted.RootElement)).IsTrue();
            await EqualAsync(row with
            {
                Json = ""
            }, golden with
            {
                Json = ""
            });
            await Assert.That(row.Json.Contains("PRIVATE-54", StringComparison.Ordinal)).IsFalse();
        }
    }
}
