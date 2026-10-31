/// <summary>Immutable execution server image identities shared by session topology registrations.</summary>
internal static class BenchmarkResources
{
    // Multi-architecture manifest digests pin the actual content, including the PostgreSQL patch under pg18.
    internal const string PostgresDigest = "sha256:2ba9ca5f2e7daa0f0e7723cba1ee9167bab54efd3640516a44ac1a928dd67e7a";
    internal const string QdrantDigest = "sha256:94728574965d17c6485dd361aa3c0818b325b9016dac5ea6afec7b4b2700865f";
    internal const string RabbitDigest = "sha256:aeee1db0ff9fdb1347f3585242c14ed10877b56c22eeaa113b21208ef3dd3edf";
    internal const string RedisDigest = "sha256:c22af04bb576503bf16b3e34a1fd2fd82de0f765afd866d2e380145e0af30d78";
    internal const string Neo4jDigest = "sha256:91fb0bf237c41b7b3dcbe84703aa0b82e0d7d067b16e1c8ab21f03fc679edf4e";

    internal const string MongoDigest = "sha256:81a1c8842a09589fc8d5f285266f3340bf4abdf66700ba22988f14cc9b2b3118";
    internal const string OpenSearchDigest = "sha256:b5dd1512af2a99748c942cfbbd7f32162623336b210667d0fc6333c6321f171d";
    internal const string KurrentDigest = "sha256:ef49a58bab8bc4d7b08cd218f1bb85cf230b03edf5d136e98d6e86a3e5100b6a";
    // Official multi-platform Docker Hub index for SurrealDB v3.2.4.
    internal const string SurrealDbDigest = "sha256:51baed8709f57f67dcf04b30e3177db846803fa9342dae2be58c6fa5f8d59843";
    // Official GHCR linux/amd64 manifest for HelixDB server v0.0.10.
    internal const string HelixDbDigest = "sha256:1bd2b9f91fe63190a4cb2c7058e680eef635e9fa9df67a4fcfcc210d3fe1f68a";

}
