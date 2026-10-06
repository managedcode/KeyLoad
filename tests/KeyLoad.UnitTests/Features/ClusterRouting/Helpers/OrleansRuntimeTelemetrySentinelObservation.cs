using System.Diagnostics;
using System.Globalization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record OrleansRuntimeTelemetrySentinelObservation(
    OrleansRuntimeTelemetryMutation Mutation,
    bool WasInjected,
    bool IsApplicationSource,
    bool IsLifecycleSource,
    bool IsOtherSource,
    bool IsRequestMethod,
    bool IsCapabilityMethod,
    bool IsGenericMethod,
    bool MatchedCallerParent,
    ActivityStatusCode StatusAtStart,
    ActivityStatusCode StatusAfterInjection,
    ActivityStatusCode StatusAtStop,
    bool RecordedAtStart,
    bool RecordedAfterInjection,
    bool RecordedAtStop,
    int BaggageCountAtStart,
    int BaggageCountAfterInjection,
    int BaggageCountAtStop,
    bool WasStopped,
    bool ExportMatched)
{
    internal string ToSafeSummary()
    {
        var fields = new[]
        {
            $"mutation={Mutation}",
            $"injected={WasInjected}",
            $"source_app={IsApplicationSource}",
            $"source_lifecycle={IsLifecycleSource}",
            $"source_other={IsOtherSource}",
            $"method_request={IsRequestMethod}",
            $"method_capability={IsCapabilityMethod}",
            $"method_generic={IsGenericMethod}",
            $"parent_match={MatchedCallerParent}",
            $"status_start={StatusAtStart}",
            $"status_injected={StatusAfterInjection}",
            $"status_stop={StatusAtStop}",
            $"recorded_start={RecordedAtStart}",
            $"recorded_injected={RecordedAfterInjection}",
            $"recorded_stop={RecordedAtStop}",
            FormatBaggage("start", BaggageCountAtStart),
            FormatBaggage("injected", BaggageCountAfterInjection),
            FormatBaggage("stop", BaggageCountAtStop),
            $"stopped={WasStopped}",
            $"export_match={ExportMatched}"
        };
        return string.Join(";", fields);
    }

    private static string FormatBaggage(string stage, int count)
        => string.Create(CultureInfo.InvariantCulture, $"baggage_{stage}={count > 0}:{count}");
}
