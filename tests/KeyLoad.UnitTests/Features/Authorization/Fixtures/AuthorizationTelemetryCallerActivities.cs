using System.Diagnostics;
using KeyLoad.ServiceDefaults.Features.Authorization.Configuration;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationTelemetryCallerActivities : IDisposable
{
    private readonly Activity[] activities;
    internal Activity? Ambient { get; }
    internal Activity First => activities[AuthorizationTelemetryTestProtocol.IndexCount];
    internal Activity? Current => activities.Length > AuthorizationTelemetryTestProtocol.IndexCount
        ? activities[^AuthorizationTelemetryTestProtocol.HealthyCount] : null;
    internal AuthorizationTelemetryCallerActivities(AuthorizationUnsafeSpanState state)
    {
        var depth = state switch
        {
            AuthorizationUnsafeSpanState.InheritedParentBaggage => AuthorizationTelemetryTestProtocol.HealthyCount,
            AuthorizationUnsafeSpanState.ExcessParentLinks => new HttpTelemetryPrivacyOptions().MaximumParentLinks
                + AuthorizationTelemetryTestProtocol.HealthyCount,
            _ => AuthorizationTelemetryTestProtocol.HealthyCount
        };
        Ambient = Activity.Current;
        activities = new Activity[depth];
        for (var index = AuthorizationTelemetryTestProtocol.IndexCount; index < depth; index++)
        {
            activities[index] = new Activity(AuthorizationTelemetryTestProtocol.CallerParent);
            if (index == AuthorizationTelemetryTestProtocol.IndexCount && Ambient is not null)
            { activities[index].SetParentId(Ambient.TraceId, Ambient.SpanId, Ambient.ActivityTraceFlags); }
            activities[index].Start();
            activities[index].ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        }
    }
    public void Dispose()
    {
        for (var index = activities.Length - AuthorizationTelemetryTestProtocol.HealthyCount;
            index >= AuthorizationTelemetryTestProtocol.IndexCount; index--)
        { activities[index].Dispose(); }
    }
}
