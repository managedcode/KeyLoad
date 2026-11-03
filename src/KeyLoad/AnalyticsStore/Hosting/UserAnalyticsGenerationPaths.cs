namespace KeyLoad.Server;

/// <summary>Uses the locked physical host root and a unique directory per admitted generation.</summary>
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
        Directory.CreateDirectory(root);
    }

    internal static string Create(PartitionHost physical)
        => Path.Combine(physical.DirectoryPath, DirectoryName, Guid.NewGuid().ToString(GenerationFormat));

    internal static void RemoveJoined(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
