using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeSixAzureMembershipConfigurator : IHostConfigurator
{
    public NativeSixAzureMembershipConfigurator() { }

    private const string MissingConnection = "The owned native membership connection is missing.";
    internal const string ConnectionName = "orleans-membership";
    internal const string ConnectionKey = "ConnectionStrings:" + ConnectionName;

    public void Configure(IHostBuilder hostBuilder)
    {
        hostBuilder.UseOrleans((context, silo) =>
        {
            var connection = context.Configuration.GetConnectionString(ConnectionName)
                ?? throw new InvalidOperationException(MissingConnection);
            silo.UseAzureStorageClustering((OptionsBuilder<global::Orleans.Clustering.AzureStorage.AzureStorageClusteringOptions> azure) =>
                azure.Configure(provider => provider.TableServiceClient = new global::Azure.Data.Tables.TableServiceClient(connection)));
        });
    }
}
