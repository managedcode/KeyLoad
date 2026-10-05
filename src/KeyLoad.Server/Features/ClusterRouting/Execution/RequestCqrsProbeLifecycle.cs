using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeLifecycle(IOptions<RequestProbeExecutionOptions> executionOptions)
{
    private readonly RequestProbeExecutionOptions settings = executionOptions.Value;
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, RequestCqrsProbeClaim> claims = [];
    private TaskCompletionSource drained = Completed();
    private int activeCallbacks;
    private int activeGates;
    private bool stopAdmission;

    internal void EnterCallback()
    {
        lock (sync)
        {
            if (stopAdmission)
            { throw Invalid(); }
            StartCallback(resetJoin: true);
        }
    }

    internal void EnterClaimedCallback()
    {
        lock (sync)
        { StartCallback(resetJoin: !stopAdmission); }
    }

    internal RequestCqrsProbeClaim? FindClaim(Guid requestId)
    {
        lock (sync)
        { return claims.GetValueOrDefault(requestId); }
    }

    internal RequestCqrsProbeClaim AddClaim(GrainRequestProbeIdentity identity, RequestCqrsProbeLoadedArm arm)
    {
        lock (sync)
        {
            if (stopAdmission || claims.ContainsKey(identity.RequestId)
                || claims.Values.Any(existing => existing.Arm.Record.ArmId == arm.Record.ArmId))
            { throw Invalid(); }
            var claim = new RequestCqrsProbeClaim(arm, identity);
            claims.Add(identity.RequestId, claim);
            return claim;
        }
    }

    internal void RemoveClaim(Guid requestId, RequestCqrsProbeClaim claim)
    {
        lock (sync)
        {
            if (claims.TryGetValue(requestId, out var existing) && ReferenceEquals(existing, claim))
            { claims.Remove(requestId); }
            CompleteJoinIfDrained();
        }
    }

    internal void ExitCallback()
    {
        lock (sync)
        {
            activeCallbacks--;
            CompleteJoinIfDrained();
        }
    }

    internal void EnterGate()
    {
        lock (sync)
        {
            if (activeGates >= settings.MaximumActiveGates)
            { throw Invalid(); }
            activeGates++;
        }
    }

    internal void ExitGate()
    {
        lock (sync)
        { activeGates--; }
    }

    internal Task StopAdmissionAndJoin()
    {
        lock (sync)
        {
            stopAdmission = true;
            return activeCallbacks == 0 ? Task.CompletedTask : drained.Task;
        }
    }

    private void StartCallback(bool resetJoin)
    {
        if (activeCallbacks == 0 && resetJoin)
        { drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); }
        activeCallbacks++;
    }

    private void CompleteJoinIfDrained()
    {
        if (stopAdmission && activeCallbacks == 0)
        { drained.TrySetResult(); }
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
