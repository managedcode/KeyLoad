using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Releases only this workload's host directory after Aspire has joined its session resources.</summary>
internal static class IsolatedDataCleanup
{
    private const string OwnershipFailure = "The selected database retained a named volume or writable host bind mount.";

    internal static Task DeleteAsync(string root, ContainerResource[] containers)
    {
        foreach (var resource in containers)
        {
            if (resource.Annotations.OfType<ContainerMountAnnotation>()
                .Any(mount => !mount.IsReadOnly && (mount.Type != ContainerMountType.Volume || mount.Source is not null)))
            {
                throw new IOException(OwnershipFailure);
            }
        }
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
        return Task.CompletedTask;
    }
}
