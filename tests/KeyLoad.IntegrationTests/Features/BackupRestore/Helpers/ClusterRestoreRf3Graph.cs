using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Configures and verifies the original native graph without owning its resource lifetime.</summary>
internal static class ClusterRestoreRf3Graph
{
    private const string PhysicalParameter = "membership-physical-b";
    private const string IncarnationParameter = "membership-incarnation-b";
    private const string PeerParameter = "membership-peer-b";
    private const string ParameterPrefix = "Parameters:";
    internal static async Task<IDistributedApplicationTestingBuilder> CreateAsync(string profileRoot,
        PhysicalShardRecord second, string peer, CancellationToken cancellationToken)
    {
        var arguments = TwoRf3WaveArguments.Create(profileRoot, null, true, true, true, protectedDocuments: true);
        var parameters = new Dictionary<string, string?>
        {
            [ParameterPrefix + PhysicalParameter] = second.PhysicalShardId.ToString(ClusterRestoreRf3Protocol.IdentityFormat),
            [ParameterPrefix + IncarnationParameter] = second.Incarnation.ToString(ClusterRestoreRf3Protocol.IdentityFormat),
            [ParameterPrefix + PeerParameter] = peer
        };
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments,
            (_, settings) =>
            {
                settings.Configuration ??= new ConfigurationManager();
                settings.Configuration.AddInMemoryCollection(parameters);
            }, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3BuilderCleanup.RequireAsync(builder, () => RequireParametersAsync(builder, parameters,
            cancellationToken)).ConfigureAwait(false);
        return builder;
    }

    private static async Task RequireParametersAsync(IDistributedApplicationTestingBuilder builder,
        Dictionary<string, string?> parameters, CancellationToken cancellationToken)
    {
        foreach (var parameter in parameters)
        {
            var name = parameter.Key[ParameterPrefix.Length..];
            var actual = await builder.Resources.OfType<ParameterResource>().Single(value => value.Name == name)
                .GetValueAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, parameter.Value, StringComparison.Ordinal))
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        }
    }

}
