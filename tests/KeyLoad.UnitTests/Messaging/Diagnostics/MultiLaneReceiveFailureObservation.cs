using System.Runtime.CompilerServices;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class MultiLaneReceiveFailureObservation
{
    private static readonly ConditionalWeakTable<RequestCqrsHostedRuntimeFixture, MultiLaneReceiveFailureCapture> Captures = new();
    internal static MultiLaneReceiveFailureCapture Get(RequestCqrsHostedRuntimeFixture fixture)
        => Captures.GetValue(fixture, static owner =>
        {
            var capture = new MultiLaneReceiveFailureCapture(owner.RoutingOptions.Value.MaximumTotalFrames);
            owner.Services.GetRequiredService<ILoggerFactory>().AddProvider(capture);
            return capture;
        });
    internal static string Summary(RequestCqrsHostedRuntimeFixture fixture)
        => Get(fixture).LatestSummary;
}
