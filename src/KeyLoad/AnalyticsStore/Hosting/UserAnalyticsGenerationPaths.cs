using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server;

/// <summary>Uses only the already locked physical host root and server-created lifetime/generation names.</summary>
internal static class UserAnalyticsGenerationPaths
{
    internal const string DirectoryName = "derived-user-analytics";
    private const string GenerationFormat = "N";

    internal static void PrepareRoot(PartitionHost physical)
    {
        var root = Path.Combine(physical.DirectoryPath, DirectoryName);
        if (Directory.Exists(root))
        {
            // No projection is authoritative or reusable across a physical owner opening.
            Directory.Delete(root, recursive: true);
        }
    }

    internal static string Create(PartitionHost physical, AnalyticsAggregateSnapshot snapshot)
    {
        var lifetime = Convert.ToHexString(SHA256.HashData(OrleansSerialization.Serialize(snapshot.Lifetime)));
        return Path.Combine(physical.DirectoryPath, DirectoryName, lifetime,
            Guid.NewGuid().ToString(GenerationFormat));
    }

    internal static void RemoveJoined(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        var lifetime = Path.GetDirectoryName(directory)!;
        if (Directory.Exists(lifetime))
        {
            try
            {
                Directory.Delete(lifetime, recursive: false);
            }
            catch (DirectoryNotFoundException)
            {
                // Another joined generation already removed the same empty parent.
            }
            catch (IOException) when (HasOtherGenerationOrGone(lifetime))
            {
                // Another admitted generation owns this lifetime directory until its own joined close.
            }
        }
    }
    private static bool HasOtherGenerationOrGone(string lifetime)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(lifetime).Any();
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
    }
}
