using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainIdentityAssertions
{
    private const string FirstDigest = "8cb5d0d496a5e895df854d65e874012037d19d4982aa8c65759ef62cced33bc6";
    private const string SecondDigest = "106e6bb32f124cae7cd4697a58db805e38e9f7382f572d220bdd023e39cb2ab5";
    private const string IsolatedDocumentDigest = "50618a701dec27ebe14e2c2e111a94d92bd4549ee05092e199ce755ed2ed26dd";
    private const string IsolatedEventDigest = "dfb6fda6d202afb8643533ccf231be93a0fe7b9d489aa584125cbf4c627b03f9";
    private const string IsolatedQueueDigest = "56ddab690f456951801e6fbcdf87fea4855c01868a2c31e3b5713acbbafb6329";
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
