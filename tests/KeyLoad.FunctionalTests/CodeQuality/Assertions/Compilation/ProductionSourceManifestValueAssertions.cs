using System.Text.Json;

namespace KeyLoad.FunctionalTests.CodeQuality.Assertions;

internal static class ProductionSourceManifestValueAssertions
{
    internal static async Task EqualAsync(JsonElement expected, JsonElement actual)
    {
        await Assert.That(actual.ValueKind).IsEqualTo(expected.ValueKind);
        if (expected.ValueKind == JsonValueKind.String)
        {
            await Assert.That(actual.GetString()).IsEqualTo(expected.GetString());
            return;
        }
        var expectedItems = expected.EnumerateArray().Select(item => item.GetString()).ToArray();
        var actualItems = actual.EnumerateArray().Select(item => item.GetString()).ToArray();
        await Assert.That(actualItems).IsEquivalentTo(expectedItems, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task FieldsAsync(JsonElement element, string[] expected)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        await Assert.That(actual).IsEquivalentTo(expected.Order(StringComparer.Ordinal),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
