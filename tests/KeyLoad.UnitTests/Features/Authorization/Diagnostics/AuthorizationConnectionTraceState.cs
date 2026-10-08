using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class AuthorizationConnectionTraceState : IDisposable
{
    private const string ConnectionSource = "Experimental.System.Net.Http.Connections";
    private const string ConnectionOperation = "Experimental.System.Net.Http.Connections.ConnectionSetup";
    private readonly ActivityListener? listener;
    internal AuthorizationConnectionTraceState(Func<AuthorizationUnsafeSpanState> state)
    {
        if (state() != AuthorizationUnsafeSpanState.ClientTraceStateLink)
        { return; }
        listener = new()
        {
            ShouldListenTo = static source => source.Name == ConnectionSource,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.PropagationData,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == ConnectionOperation && state() == AuthorizationUnsafeSpanState.ClientTraceStateLink)
                { activity.TraceStateString = AuthorizationTelemetryTestProtocol.TraceStateCanary; }
            }
        };
        try
        { ActivitySource.AddActivityListener(listener); }
        catch (Exception)
        { listener.Dispose(); throw; }
    }
    public void Dispose() => listener?.Dispose();
}
