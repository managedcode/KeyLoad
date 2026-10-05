using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PhysicalShardCatalogInterface34Images(string Interface3, string Interface4)
{
    internal static async Task<PhysicalShardCatalogInterface34Images> ReadAsync(CancellationToken cancellationToken)
    {
        var prior = await RequestCqrsRf3Interface3ImageProof.ReadVerifiedReferenceAsync(cancellationToken)
            .ConfigureAwait(false);
        var current = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken)
            .ConfigureAwait(false);
        return new(prior, current);
    }

    internal IReadOnlyDictionary<string, string> AllInterface3() => All(Interface3);

    internal IReadOnlyDictionary<string, string> AllInterface4() => All(Interface4);

    internal IReadOnlyDictionary<string, string> Mixed()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = Interface4,
            [RequestCqrsRf3Protocol.Node2] = Interface3,
            [RequestCqrsRf3Protocol.Node3] = Interface3
        };

    private static Dictionary<string, string> All(string reference)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = reference,
            [RequestCqrsRf3Protocol.Node2] = reference,
            [RequestCqrsRf3Protocol.Node3] = reference
        };
}
