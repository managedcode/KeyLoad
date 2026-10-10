using System.Runtime.CompilerServices;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class MultiLaneReceiveFailureObservation
{
    private static readonly ConditionalWeakTable<RequestCqrsClusterFixture, MultiLaneReceiveFailureCapture> Captures = new();
    internal static MultiLaneReceiveFailureCapture Get(RequestCqrsClusterFixture fixture)
        => Captures.GetValue(fixture, static owner =>
        {
            var capture = new MultiLaneReceiveFailureCapture(owner.RoutingOptions.Value.MaximumTotalFrames);
            owner.Cluster.GetSiloServiceProvider().GetRequiredService<ILoggerFactory>().AddProvider(capture);
            return capture;
        });
    internal static string Summary(RequestCqrsClusterFixture fixture)
        => Get(fixture).LatestSummary;
}
