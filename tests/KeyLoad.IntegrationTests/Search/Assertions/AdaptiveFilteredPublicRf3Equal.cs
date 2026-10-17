using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Equal
{
    internal static async Task RequireAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual)).IsEquivalentTo(JsonDefaults.Serialize(expected), CollectionOrdering.Matching);
}
