using System.Security.Cryptography;

namespace KeyLoad.Orleans;

internal static class TextMaintenanceChildIdentity
{
    private const int Start = 0;
    private const int GuidBytes = 16;

    internal static Guid Create(Guid parent, TextIndexMaintenancePhase phase, long sequence)
        => new(SHA256.HashData(NativeSerialization.Serialize(new Identity(parent, phase, sequence))).AsSpan(Start, GuidBytes));

    [global::Orleans.GenerateSerializer, global::Orleans.Alias(TextMaintenanceCapabilityAliases.ChildIdentity)]
    internal sealed record Identity([property: global::Orleans.Id(0)] Guid Parent,
        [property: global::Orleans.Id(1)] TextIndexMaintenancePhase Phase,
        [property: global::Orleans.Id(2)] long Sequence);
}
