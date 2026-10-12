using Aspire.Hosting.Azure;
using KeyLoad.Orleans;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class NativeClusteringResources
{
    private const string StorageName = "orleans-storage";
    private const string TablesName = "orleans-membership";

    internal static IResourceBuilder<AzureTableStorageResource> Add(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddAzureStorage(StorageName).RunAsEmulator().AddTables(TablesName);
    }

    internal static void Apply(IResourceBuilder<ContainerResource> silo,
        IResourceBuilder<AzureTableStorageResource> tables)
    {
        silo.WithReference(tables, connectionName: OrleansHostingOptions.ConnectionName).WaitFor(tables);
    }
}
