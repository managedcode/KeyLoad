using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class BenchmarkCallerBindings
{
    private const string HttpEndpoint = "http";
    private const string PeerSettingPrefix = "Benchmarks__KeyLoadEndpoints__";
    private const string RabbitManagementSetting = "Benchmarks__RabbitManagementEndpoint";
    private const string RabbitUserSetting = "Benchmarks__RabbitUser";
    private const string RabbitPasswordSetting = "Benchmarks__RabbitPassword";
    private const string InvalidNodeCount = "The comparison caller requires three actual RF3 nodes.";
    private const int RequiredNodeCount = 3;

    internal static void Apply(IResourceBuilder<ContainerResource> runner,
        IResourceBuilder<ContainerResource>[] nodes, IResourceBuilder<RabbitMQServerResource> rabbit)
    {
        const int IndexInitialValue = 0;

        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(rabbit);
        if (nodes.Length != RequiredNodeCount)
        {
            throw new ArgumentException(InvalidNodeCount, nameof(nodes));
        }

        for (var index = IndexInitialValue; index < nodes.Length; index++)
        {
            runner.WithEnvironment(PeerSettingPrefix + index.ToString(CultureInfo.InvariantCulture),
                nodes[index].GetEndpoint(HttpEndpoint));
        }

        runner.WithEnvironment(RabbitManagementSetting, rabbit.Resource.ManagementEndpoint)
            .WithEnvironment(RabbitUserSetting, rabbit.Resource.UserNameReference)
            .WithEnvironment(RabbitPasswordSetting, rabbit.Resource.PasswordParameter);
    }
}
