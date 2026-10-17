using Aspire.Hosting.Azure;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Composes the owned native membership authority used by a six-silo restart control.</summary>
internal static class NativeSixMembershipResources
{
    /// <summary>Builds one ephemeral Aspire-owned Azurite application without starting database nodes.</summary>
    /// <returns>The owned application and its discovered native table connection resource.</returns>
    public static (DistributedApplication Application, AzureTableStorageResource Tables) Create()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            DisableDashboard = true,
            AssemblyName = typeof(NativeSixMembershipResources).Assembly.GetName().Name
        });
        var tables = NativeClusteringResources.Add(builder);
        return (builder.Build(), tables.Resource);
    }
}
