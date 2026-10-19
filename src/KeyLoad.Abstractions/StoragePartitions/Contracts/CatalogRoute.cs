namespace KeyLoad;

/// <summary>Defines the shared logical Orleans address for database metadata operations.</summary>
public static class CatalogRoute
{
    /// <summary>The logical metadata actor and commit receipt scope.</summary>
    public const string PartitionId = "keyload:catalog";
}
