using System.Diagnostics;
using KeyLoad.ServiceDefaults.Features.ClusterRouting.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class OrleansRuntimeTelemetryAssertions
{
    internal static async Task AssertIsolatedParentAsync(Activity parent)
    {
        await Assert.That(parent.Parent).IsNull();
        await Assert.That(parent.TraceId).IsNotEqualTo(default(ActivityTraceId));
        await Assert.That(parent.ParentSpanId).IsNotEqualTo(default(ActivitySpanId));
        await Assert.That((parent.ActivityTraceFlags & ActivityTraceFlags.Recorded) != ActivityTraceFlags.None).IsTrue();
        await Assert.That(parent.Baggage.Any()).IsFalse();
        await Assert.That(ReferenceEquals(Activity.Current, parent)).IsTrue();
    }

    internal static async Task AssertActivityRestoredAsync(Activity? expected)
        => await Assert.That(ReferenceEquals(Activity.Current, expected)).IsTrue();

    internal static async Task AssertTracePrivacyAsync(OrleansActivityCapture[] captures, PrincipalRecord first,
        PrincipalRecord second, PrincipalRecord third, IReadOnlyList<OrleansTelemetryOperationParent> parents,
        OrleansTelemetryOptions options, IReadOnlyList<OrleansRuntimeTelemetrySentinelObservation> sentinelObservations)
    {
        var spans = captures.Where(static span => span.SourceName is OrleansRuntimeTelemetryTokens.ApplicationSource
            or OrleansRuntimeTelemetryTokens.LifecycleSource).ToArray();
        await Assert.That(spans.Length).IsGreaterThan(0);
        await Assert.That(spans.Any(span => span.Tags.Any(tag => tag.Key == OrleansRuntimeTelemetryTokens.RpcMethodTag
            && tag.Value == OrleansRuntimeTelemetryTokens.RequestMethodValue))).IsTrue();
        await Assert.That(spans.Any(static span => span.Status == System.Diagnostics.ActivityStatusCode.Error)).IsTrue()
            .Because(string.Join(Environment.NewLine,
                sentinelObservations.Select(static observation => observation.ToSafeSummary())));
        foreach (var parent in parents)
        {
            await Assert.That(spans.Any(span => span.TraceId == parent.TraceId.ToHexString()
                && span.ParentSpanId == parent.ParentSpanId.ToHexString())).IsTrue();
        }

        var exported = string.Join("\n", spans.Select(Format));
        await Assert.That(exported).DoesNotContain(OrleansRuntimeTelemetryTokens.SentinelValue);
        await Assert.That(exported).DoesNotContain(first.Id);
        await Assert.That(exported).DoesNotContain(second.Id);
        await Assert.That(exported).DoesNotContain(third.Id);
        await Assert.That(exported).DoesNotContain(OrleansRuntimeTelemetryTokens.DocumentJson);
        foreach (var span in spans)
        {
            await AssertSafeSpanAsync(span, options);
        }
    }

    internal static async Task AssertMetricPrivacyAsync(OrleansMetricPointCapture[] metrics)
    {
        var native = metrics.Where(static metric => metric.MeterName == OrleansRuntimeTelemetryTokens.MetricMeter).ToArray();
        await Assert.That(native.Length).IsGreaterThan(0);
        foreach (var point in native)
        {
            await Assert.That(point.Tags.Length).IsEqualTo(0);
            await Assert.That(point.ExemplarTags.Length).IsEqualTo(0);
        }

        await AssertSuppressionReasonAsync(metrics, OrleansRuntimeTelemetryTokens.SafeSuppressionReason);
        await AssertSuppressionReasonAsync(metrics, OrleansRuntimeTelemetryTokens.BaggageSuppressionReason);
        await AssertSuppressionReasonAsync(metrics, OrleansRuntimeTelemetryTokens.LinkSuppressionReason);
    }

    private static async Task AssertSafeSpanAsync(OrleansActivityCapture span, OrleansTelemetryOptions options)
    {
        var expectedDisplayName = span.SourceName == OrleansRuntimeTelemetryTokens.ApplicationSource
            ? OrleansRuntimeTelemetryTokens.ApplicationDisplayName : OrleansRuntimeTelemetryTokens.LifecycleDisplayName;
        await Assert.That(span.DisplayName).IsEqualTo(expectedDisplayName);
        await Assert.That(span.StatusDescription).IsNullOrEmpty();
        await Assert.That(span.TraceState).IsNullOrEmpty();
        await Assert.That(span.Baggage.Length).IsEqualTo(0);
        await Assert.That(span.HasLinks).IsFalse();
        await Assert.That(span.Tags.Length).IsLessThanOrEqualTo(options.MaximumTags);
        await Assert.That(span.Events.Length).IsLessThanOrEqualTo(options.MaximumEvents);
        await Assert.That(span.Tags.All(IsSafeTag)).IsTrue();
        await Assert.That(span.Events.All(IsSafeEvent)).IsTrue();
    }

    private static async Task AssertSuppressionReasonAsync(OrleansMetricPointCapture[] metrics, string reason)
    {
        var present = metrics.Any(point => point.MeterName == OrleansRuntimeTelemetryTokens.PrivacyMeter
            && point.MetricName == OrleansRuntimeTelemetryTokens.SuppressionCounter
            && point.Tags.Any(tag => tag.Key == OrleansRuntimeTelemetryTokens.SuppressionReasonTag
                && tag.Value == reason));
        await Assert.That(present).IsTrue();
    }

    private static bool IsSafeTag(KeyValuePair<string, string?> tag)
        => tag.Key switch
        {
            OrleansRuntimeTelemetryTokens.RpcSystemTag => tag.Value == OrleansRuntimeTelemetryTokens.RpcSystemValue,
            OrleansRuntimeTelemetryTokens.RpcServiceTag => tag.Value == OrleansRuntimeTelemetryTokens.RpcServiceValue,
            OrleansRuntimeTelemetryTokens.RpcMethodTag => tag.Value is OrleansRuntimeTelemetryTokens.RequestMethodValue
                or OrleansRuntimeTelemetryTokens.CapabilityMethodValue or OrleansRuntimeTelemetryTokens.GenericMethodValue,
            OrleansRuntimeTelemetryTokens.GrainTypeTag => tag.Value == OrleansRuntimeTelemetryTokens.GrainTypeValue,
            OrleansRuntimeTelemetryTokens.ActivationCauseTag => tag.Value is OrleansRuntimeTelemetryTokens.ActivationCauseNewValue
                or OrleansRuntimeTelemetryTokens.ActivationCauseRehydrateValue,
            OrleansRuntimeTelemetryTokens.ExceptionTypeTag => tag.Value == OrleansRuntimeTelemetryTokens.ExceptionTypeValue,
            OrleansRuntimeTelemetryTokens.ExceptionEscapedTag => bool.TryParse(tag.Value, out _),
            _ => false
        };

    private static bool IsSafeEvent(OrleansActivityEventCapture activityEvent)
        => activityEvent.Tags.Length == 0 && activityEvent.Name is OrleansRuntimeTelemetryTokens.EventInstanceCreated
            or OrleansRuntimeTelemetryTokens.EventRehydrated or OrleansRuntimeTelemetryTokens.EventActivationStart
            or OrleansRuntimeTelemetryTokens.EventDirectoryRetryRecovery or OrleansRuntimeTelemetryTokens.EventRetryRecovery
            or OrleansRuntimeTelemetryTokens.EventDirectoryRegisterSuccess or OrleansRuntimeTelemetryTokens.EventSuccess
            or OrleansRuntimeTelemetryTokens.EventDirectoryRetryPrevious or OrleansRuntimeTelemetryTokens.EventRetryPrevious
            or OrleansRuntimeTelemetryTokens.EventDuplicateActivation or OrleansRuntimeTelemetryTokens.EventDuplicate
            or OrleansRuntimeTelemetryTokens.EventDirectoryRegisterFailed or OrleansRuntimeTelemetryTokens.EventStateActivating
            or OrleansRuntimeTelemetryTokens.EventLifecycleStart or OrleansRuntimeTelemetryTokens.EventLifecycleStarted
            or OrleansRuntimeTelemetryTokens.EventLifecycleStartFailed or OrleansRuntimeTelemetryTokens.EventStateValid;

    private static string Format(OrleansActivityCapture activity)
        => string.Join("|", activity.SourceName, activity.DisplayName, activity.Status.ToString(),
            activity.StatusDescription, activity.TraceState, string.Join(",", activity.Baggage),
            string.Join(",", activity.Tags.Select(static tag => tag.Key + "=" + tag.Value)),
            string.Join(",", activity.Events.Select(static item => item.Name + ":" + string.Join(",",
                item.Tags.Select(static tag => tag.Key + "=" + tag.Value)))));
}
