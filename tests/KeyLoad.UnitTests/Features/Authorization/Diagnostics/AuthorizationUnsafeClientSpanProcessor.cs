using System.Diagnostics;
using OpenTelemetry;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationUnsafeClientSpanProcessor(Func<AuthorizationUnsafeSpanState> state) : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity.Source.Name != AuthorizationTelemetryTestProtocol.HttpClientSource)
        { return; }
        var selected = state();
        if (selected == AuthorizationUnsafeSpanState.ClientEvent)
        { activity.AddEvent(new(AuthorizationTelemetryTestProtocol.ExceptionCanary)); }
        if (selected is AuthorizationUnsafeSpanState.ClientTaggedLink or AuthorizationUnsafeSpanState.ClientExtraLink)
        {
            var context = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded,
                null);
            var tags = selected == AuthorizationUnsafeSpanState.ClientTaggedLink ? new ActivityTagsCollection
                { { AuthorizationTelemetryTestProtocol.CanaryTag, AuthorizationTelemetryTestProtocol.TagCanary } } : null;
            activity.AddLink(new(context, tags));
        }
    }
}
