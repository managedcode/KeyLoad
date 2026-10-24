using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Messaging;

internal static class MultiLaneReceiveFailureObservation
{
    private static readonly ConditionalWeakTable<RequestCqrsHostedRuntimeFixture, MultiLaneReceiveFailureCapture> Captures = new();
    internal static MultiLaneReceiveFailureCapture Get(RequestCqrsHostedRuntimeFixture fixture)
        => Captures.GetValue(fixture, static owner =>
        {
            var capture = new MultiLaneReceiveFailureCapture(owner.RoutingOptions.Value.MaximumStreamFrames);
            owner.Services.GetRequiredService<ILoggerFactory>().AddProvider(capture);
            return capture;
        });
    internal static string Summary(RequestCqrsHostedRuntimeFixture fixture)
        => Get(fixture).LatestSummary;
}
