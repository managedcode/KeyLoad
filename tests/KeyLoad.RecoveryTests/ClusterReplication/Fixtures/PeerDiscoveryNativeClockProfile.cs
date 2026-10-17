using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryNativeClockProfile : IDisposable
{
    internal const string ColdBeforeJoined = "All original native node lifetimes must join before original roots reopen.";
    internal const string PrincipalId = "recorded-clock-owner";
    private const string TenantId = "system";
    private const string Wildcard = "*";
    private const int SigningKeyBytes = 32;
    private const int CredentialEntropyBytes = 32;
    private readonly byte[] initialSigningKey = RandomNumberGenerator.GetBytes(SigningKeyBytes);
    private readonly string credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(CredentialEntropyBytes));
    private readonly IReadOnlyDictionary<string, TimeProvider> clocks;
    internal ReadOnlyMemory<byte> InitialSigningKey => initialSigningKey;

    internal PeerDiscoveryNativeClockProfile(IReadOnlyDictionary<string, TimeProvider> canonicalClocks)
        => clocks = canonicalClocks;

    internal TimeProvider ClockFor(string voter) => clocks[voter];

    public void Dispose() => CryptographicOperations.ZeroMemory(initialSigningKey);

    internal void Bootstrap(DatabaseEngine database, ImmutableArray<string> voters)
    {
        database.Bootstrap(new(PrincipalId, TenantId, [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
        {
            ClusterAdministrator = true
        }, DatabaseEngine.Credential(PrincipalId, PrincipalId, credential));
        RecoveryPhysicalShardBootstrap.Bootstrap(database, PrincipalId, voters);
    }
}
