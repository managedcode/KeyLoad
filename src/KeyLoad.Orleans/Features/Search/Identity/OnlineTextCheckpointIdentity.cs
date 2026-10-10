using System.Security.Cryptography;

namespace KeyLoad.Orleans;

internal static class OnlineTextCheckpointIdentity
{
    private const int GuidBytes = 16;
    private const int DigestStart = 0;
    private const string IdentityAlias = "keyload.orleans.search.online-text-checkpoint-identity.v1";
    internal static Guid Create(Guid parent, long sequence)
        => new(SHA256.HashData(NativeSerialization.Serialize(new Identity(parent, sequence))).AsSpan(DigestStart, GuidBytes));

    [global::Orleans.GenerateSerializer, global::Orleans.Alias(IdentityAlias)]
    internal sealed record Identity([property: global::Orleans.Id(0)] Guid Parent,
        [property: global::Orleans.Id(1)] long Sequence);
}
