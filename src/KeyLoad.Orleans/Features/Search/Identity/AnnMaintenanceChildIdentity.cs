using System.Security.Cryptography;

namespace KeyLoad.Orleans;

internal static class AnnMaintenanceChildIdentity
{
    private const string Alias = "keyload.orleans.ann-child-identity.v1";
    private const int Start = 0;
    private const int GuidBytes = 16;

    internal static Guid Create(Guid parent, AnnMaintenancePhase phase, long sequence)
        => new(SHA256.HashData(NativeSerialization.Serialize(new Identity(parent, phase, sequence))).AsSpan(Start, GuidBytes));

    [global::Orleans.GenerateSerializer, global::Orleans.Alias(Alias)]
    internal sealed record Identity([property: global::Orleans.Id(0)] Guid Parent,
        [property: global::Orleans.Id(1)] AnnMaintenancePhase Phase,
        [property: global::Orleans.Id(2)] long Sequence);
}
