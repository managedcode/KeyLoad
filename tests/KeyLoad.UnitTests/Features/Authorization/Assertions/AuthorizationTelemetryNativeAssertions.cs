using KeyLoad.ServiceDefaults.Features.Authorization.Contracts;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryNativeAssertions
{
    internal static async Task CallerAsync(AuthorizationTelemetryCallerActivities callers)
    {
        await Assert.That(callers.First.Parent).IsNull();
        await Assert.That(callers.First.Baggage).IsEmpty();
        if (callers.Ambient is { } ambient)
        {
            await Assert.That(callers.First.TraceId).IsEqualTo(ambient.TraceId);
            await Assert.That(callers.First.ParentSpanId).IsEqualTo(ambient.SpanId);
        }
        await Assert.That(callers.Current!.Recorded).IsTrue();
    }
    internal static async Task SuppressionAsync(AuthorizationNativeSpanState[] nativeSpans, AuthorizationSuppressionCapture[] suppressed, AuthorizationUnsafeSpanState state, string? suppressedTrace)
    {
        if (state is AuthorizationUnsafeSpanState.ClientEvent or AuthorizationUnsafeSpanState.ClientTaggedLink
            or AuthorizationUnsafeSpanState.ClientTraceStateLink or AuthorizationUnsafeSpanState.ClientExtraLink)
        {
            var reason = state == AuthorizationUnsafeSpanState.ClientExtraLink ? HttpTelemetryPrivacyPolicy.ExcessStateReason : HttpTelemetryPrivacyPolicy.UnsafeStateReason;
            await Assert.That(suppressed.Length).IsGreaterThan(AuthorizationTelemetryTestProtocol.IndexCount);
            await Assert.That(suppressed.Max(static item => item.Count)).IsEqualTo((long)AuthorizationTelemetryTestProtocol.HealthyCount);
            await Assert.That(suppressed.All(item => item.Tags.Length == AuthorizationTelemetryTestProtocol.CatalogCount
                && item.Tags.Single().Key == HttpTelemetryPrivacyPolicy.ReasonTag && item.Tags.Single().Value == reason)).IsTrue();
            var unsafeClient = nativeSpans.Single(item => item.Source == AuthorizationTelemetryTestProtocol.HttpClientSource && item.TraceId == suppressedTrace && HasUnsafeClientState(item, state));
            if (state is AuthorizationUnsafeSpanState.ClientTaggedLink or AuthorizationUnsafeSpanState.ClientExtraLink)
            {
                await Assert.That(unsafeClient.Events).IsEmpty();
                await Assert.That(unsafeClient.Links.Length).IsEqualTo(AuthorizationTelemetryTestProtocol.RequestCount);
                await Assert.That(unsafeClient.Links.Count(static link => link.HasTags)).IsEqualTo(state == AuthorizationUnsafeSpanState.ClientTaggedLink
                    ? AuthorizationTelemetryTestProtocol.CatalogCount : AuthorizationTelemetryTestProtocol.IndexCount);
                await Assert.That(unsafeClient.Links.All(static link => link.TraceState is null)).IsTrue();
            }
        }
        if (state == AuthorizationUnsafeSpanState.ClientTraceStateLink)
        {
            var unsafeClient = nativeSpans.Single(item => item.Source == AuthorizationTelemetryTestProtocol.HttpClientSource && item.TraceId == suppressedTrace && HasUnsafeClientState(item, state));
            await Assert.That(unsafeClient.Events).IsEmpty();
            var offending = unsafeClient.Links.Single();
            await Assert.That(offending.HasTags).IsFalse();
            await Assert.That(offending.TraceState).IsEqualTo(AuthorizationTelemetryTestProtocol.TraceStateCanary);
        }
    }
    private static bool HasUnsafeClientState(AuthorizationNativeSpanState span, AuthorizationUnsafeSpanState state) => state switch
    {
        AuthorizationUnsafeSpanState.ClientEvent => span.Events.Contains(AuthorizationTelemetryTestProtocol.ExceptionCanary, StringComparer.Ordinal),
        AuthorizationUnsafeSpanState.ClientTaggedLink => span.Links.Any(static link => link.HasTags),
        AuthorizationUnsafeSpanState.ClientTraceStateLink => span.Links.Any(static link => link.TraceState == AuthorizationTelemetryTestProtocol.TraceStateCanary),
        AuthorizationUnsafeSpanState.ClientExtraLink => span.Links.Length > AuthorizationTelemetryTestProtocol.CatalogCount,
        _ => false
    };
    internal static async Task IdentityAsync(AuthorizationSpanCapture[] spans, AuthorizationNativeSpanState[] nativeSpans)
    {
        foreach (var span in spans)
        {
            var original = nativeSpans.Single(item => item.Source == span.Source && item.SpanId == span.SpanId);
            if (span.Source == AuthorizationTelemetryTestProtocol.HttpClientSource)
            {
                await Assert.That(original.LocalParentCount).IsEqualTo(AuthorizationTelemetryTestProtocol.CatalogCount);
                await Assert.That(original.Baggage).IsEmpty();
            }
            await Assert.That(span.TraceId).IsEqualTo(original.TraceId);
            await Assert.That(span.ParentSpanId).IsEqualTo(original.ParentSpanId);
            await Assert.That(span.Links.Select(static link => link.TraceId + link.SpanId).ToArray())
                .IsEquivalentTo(original.Links.Select(static link => link.TraceId + link.SpanId).ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }
}
