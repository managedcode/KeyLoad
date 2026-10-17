using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Applies the complete typed database limit set to each owned Aspire node.</summary>
internal static class ClusterFixtureDatabaseLimits
{
    internal static DatabaseLimits Validate(DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        return limits;
    }

    internal static void Configure(IDistributedApplicationTestingBuilder builder, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(limits);
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            ConfigureNode(node, limits);
        }
    }

    private static void ConfigureNode(IResourceBuilder<ContainerResource> node, DatabaseLimits limits)
    {
        Set(node, nameof(DatabaseLimits.MaxDocumentBytes), limits.MaxDocumentBytes);
        Set(node, nameof(DatabaseLimits.MaxJsonDepth), limits.MaxJsonDepth);
        Set(node, nameof(DatabaseLimits.MaxBatchMutations), limits.MaxBatchMutations);
        Set(node, nameof(DatabaseLimits.MaxBatchBytes), limits.MaxBatchBytes);
        Set(node, nameof(DatabaseLimits.MaxScanRecords), limits.MaxScanRecords);
        Set(node, nameof(DatabaseLimits.MaxResults), limits.MaxResults);
        Set(node, nameof(DatabaseLimits.WriterQueueCapacity), limits.WriterQueueCapacity);
        Set(node, nameof(DatabaseLimits.MaxConcurrentQueries), limits.MaxConcurrentQueries);
        Set(node, nameof(DatabaseLimits.MaxQueryBytes), limits.MaxQueryBytes);
        Set(node, nameof(DatabaseLimits.MaxQueryDepth), limits.MaxQueryDepth);
        Set(node, nameof(DatabaseLimits.MaxQueryTokens), limits.MaxQueryTokens);
        Set(node, nameof(DatabaseLimits.QueryDeadlineSeconds), limits.QueryDeadlineSeconds);
        Set(node, nameof(DatabaseLimits.MaxQueryReadBytes), limits.MaxQueryReadBytes);
        Set(node, nameof(DatabaseLimits.MaxSearchTextTokens), limits.MaxSearchTextTokens);
        Set(node, nameof(DatabaseLimits.MaxOutboxRecords), limits.MaxOutboxRecords);
        Set(node, nameof(DatabaseLimits.MaxOutboxBytes), limits.MaxOutboxBytes);
        Set(node, nameof(DatabaseLimits.ReservedOutboxRecords), limits.ReservedOutboxRecords);
        Set(node, nameof(DatabaseLimits.ReservedOutboxBytes), limits.ReservedOutboxBytes);
        Set(node, nameof(DatabaseLimits.MaxProjectionConsumers), limits.MaxProjectionConsumers);
        Set(node, nameof(DatabaseLimits.MaxProjectionBatchBytes), limits.MaxProjectionBatchBytes);
    }

    private static void Set<T>(IResourceBuilder<ContainerResource> node, string property, T value)
        where T : struct, IFormattable
        => node.WithEnvironment(ClusterFixtureProtocol.DatabaseLimitsSettingPrefix + property,
            value.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
}
