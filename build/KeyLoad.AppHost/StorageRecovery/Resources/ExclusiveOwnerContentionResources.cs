namespace KeyLoad.AppHost.Features.StorageRecovery;

/// <summary>Adds three explicit competing processes without adding voters or independent database directories.</summary>
internal static class ExclusiveOwnerContentionResources
{
    internal const string OwnerName = "node1";
    private const string First = "file-owner-contender1";
    private const string Second = "file-owner-contender2";
    private const string Third = "file-owner-contender3";
    internal static string[] ResourceNames => [First, Second, Third];

    internal static IResourceBuilder<ProjectResource>[] Add(bool enabled, IResourceBuilder<ProjectResource> owner,
        Func<string, IResourceBuilder<ProjectResource>> create)
    {
        if (!enabled)
        {
            return [];
        }
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(create);
        return ResourceNames.Select(name => create(name).WithExplicitStart().WaitFor(owner)).ToArray();
    }
}
