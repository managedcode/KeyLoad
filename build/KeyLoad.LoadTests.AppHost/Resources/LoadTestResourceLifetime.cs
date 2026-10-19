namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

/// <summary>Uses session containers and anonymous execution volumes for the selected workload's disposable database storage.</summary>
internal static class LoadTestResourceLifetime
{
    internal static void Apply(IDistributedApplicationBuilder builder)
    {
        foreach (var resource in builder.Resources.OfType<ContainerResource>())
        {
            var container = builder.CreateResourceBuilder(resource).WithLifetime(ContainerLifetime.Session);
            foreach (var mount in resource.Annotations.OfType<ContainerMountAnnotation>()
                .Where(mount => !mount.IsReadOnly).ToArray())
            {
                resource.Annotations.Remove(mount);
                container.WithVolume(mount.Target);
            }
        }
    }
}
