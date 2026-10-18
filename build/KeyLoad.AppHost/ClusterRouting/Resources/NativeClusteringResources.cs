using Aspire.Hosting.Azure;
using KeyLoad.Clustering;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class NativeClusteringResources
{
    private const string StorageName = "orleans-storage";
    private const string TablesName = "orleans-membership";

    internal static IResourceBuilder<AzureTableStorageResource> Add(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddAzureStorage(StorageName).RunAsEmulator(emulator => emulator.WithLifetime(ContainerLifetime.Session)).AddTables(TablesName);
    }

    internal static void Apply(IResourceBuilder<ProjectResource> silo,
        IResourceBuilder<AzureTableStorageResource> tables)
    {
        silo.WithReference(tables, connectionName: OrleansHostingOptions.ConnectionName).WaitFor(tables);
    }
}
