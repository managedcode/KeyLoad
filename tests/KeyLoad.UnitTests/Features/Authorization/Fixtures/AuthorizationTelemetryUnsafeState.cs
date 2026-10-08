using System.Diagnostics;
using KeyLoad.ServiceDefaults.Features.Authorization.Configuration;

namespace KeyLoad.UnitTests.Features.Authorization;

internal enum AuthorizationUnsafeSpanState { None, Event, Link, ExcessTags, ExcessBaggage, InheritedParentBaggage, ExcessParentLinks, ClientEvent, ClientTaggedLink, ClientTraceStateLink, ClientExtraLink }
internal static class AuthorizationTelemetryUnsafeState
{
    internal static void Apply(Activity? activity, AuthorizationUnsafeSpanState state)
    {
        if (activity is null)
        { return; }
        var limits = new HttpTelemetryPrivacyOptions();
        switch (state)
        {
            case AuthorizationUnsafeSpanState.Event:
                activity.AddEvent(new(AuthorizationTelemetryTestProtocol.ExceptionCanary));
                break;
            case AuthorizationUnsafeSpanState.Link:
                activity.AddLink(new(new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded,
                    AuthorizationTelemetryTestProtocol.BaggageCanary), new ActivityTagsCollection
                    { { AuthorizationTelemetryTestProtocol.CanaryTag, AuthorizationTelemetryTestProtocol.TagCanary } }));
                break;
            case AuthorizationUnsafeSpanState.ExcessTags:
                for (var index = AuthorizationTelemetryTestProtocol.IndexCount; index <= limits.MaximumTags; index++)
                { activity.SetTag(AuthorizationTelemetryTestProtocol.CanaryTag + index, AuthorizationTelemetryTestProtocol.TagCanary); }
                break;
            case AuthorizationUnsafeSpanState.ExcessBaggage:
                for (var index = AuthorizationTelemetryTestProtocol.IndexCount; index <= limits.MaximumBaggageItems; index++)
                { activity.AddBaggage(AuthorizationTelemetryTestProtocol.BaggageKey + index, AuthorizationTelemetryTestProtocol.BaggageCanary); }
                break;
        }
    }
}
