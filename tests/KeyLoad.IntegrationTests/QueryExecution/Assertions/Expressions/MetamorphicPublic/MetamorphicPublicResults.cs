using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class MetamorphicPublicResults
{
    internal static async Task PageAsync(QueryPage actual, QueryRow[] expected, string path, bool final, long? cut = null)
    {
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        if (cut is not null)
        {
            await Assert.That(actual.CutPosition).IsEqualTo(cut.Value);
        }
        await Assert.That(actual.AccessPath).IsEqualTo(path);
        await Assert.That(actual.Cursor is null).IsEqualTo(final);
        await Assert.That(actual.Rows.Length).IsEqualTo(expected.Length);
        foreach (var (row, golden) in actual.Rows.Zip(expected))
        {
            using var value = JsonDocument.Parse(row.Json);
            using var wanted = JsonDocument.Parse(golden.Json);
            await Assert.That(JsonElement.DeepEquals(value.RootElement, wanted.RootElement)).IsTrue();
            await SqlClusterProtocol.EqualAsync(golden with
            {
                Json = ""
            }, row with
            {
                Json = ""
            });
        }
    }
}
