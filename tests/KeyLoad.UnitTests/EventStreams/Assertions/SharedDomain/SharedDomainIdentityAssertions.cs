using static KeyLoad.RecoveryHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainIdentityAssertions
{
    private const string FirstDigest = "b94add07b9cd112d807423b686139acbd9b3fdf1fd3bbf31e115ab7168ff072c";
    private const string SecondDigest = "1eae4ff3d11d8620b31dd9c95929accb5b37a3c89305fe7247cf4f46353e84e2";
    private const string IsolatedDocumentDigest = "e4ded82cc6ba9736b4ab478f1358721b508450c898dd9ffee8e249bdceba9716";
    private const string IsolatedEventDigest = "5b72d83b697d2ef6406f5b580641d24474a1a6082eb008b676e913699c4e03c4";
    private const string IsolatedQueueDigest = "6049e3e11593f368cd871c18c7d04752ee6fe8c7bb1e1ee5e1376ed5b134d9e1";
    internal static async Task RequireAsync(SharedDomainState state)
    {
        await Assert.That(state.Scopes.Select(scope => scope.Partition.AtomicPartitionId))
            .IsEquivalentTo(new[] { FirstDigest, SecondDigest, IsolatedDocumentDigest, IsolatedEventDigest, IsolatedQueueDigest },
                TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(state.Scopes.Select(scope => scope.Partition.PartitionKey).Distinct())
            .IsEquivalentTo(new[] { LiteralKey });
        await Assert.That(state.Scopes.SelectMany(scope => scope.Resources).Select(resource => resource.Name).Distinct().Count())
            .IsEqualTo(ResourceCount);
    }
    private const int ResourceCount = 9;
}
