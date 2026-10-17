namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Selects the actual immutable image for a native topology reported unavailable.</summary>
internal static class IsolatedUnsupportedTargetImage
{
    private const string SurrealDbTargetName = "SurrealDB";
    private const string HelixDbTargetName = "HelixDB";

    internal static string For(string target) => target switch
    {
        SurrealDbTargetName => IsolatedSurrealDbResources.ImageReference + BenchmarkResources.SurrealDbDigest,
        HelixDbTargetName => IsolatedHelixDbResources.ImageReference + BenchmarkResources.HelixDbDigest,
        _ => IsolatedNeo4jResources.ImageReference
    };
}
