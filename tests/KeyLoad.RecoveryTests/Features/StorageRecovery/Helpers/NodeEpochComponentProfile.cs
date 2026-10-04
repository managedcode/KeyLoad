using System.Security.Cryptography;
using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class NodeEpochComponentProfile
{
    private static readonly string[] Voters =
    [
        "http://127.0.0.1:55101/",
        "http://127.0.0.1:55102/",
        "http://127.0.0.1:55103/"
    ];

    internal static EpochPriorNodeProfile Create()
        => new(Guid.NewGuid(), Secret(), "root." + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Voters[0], Voters.ToArray());

    internal static ZoneTreeStoreOptions CanonicalStoreOptions(EpochPriorNodeProfile profile, string directory)
        => new(directory)
        {
            Incarnation = profile.Incarnation,
            SigningKey = Convert.FromBase64String(profile.SigningKey)
        };

    private static string Secret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
