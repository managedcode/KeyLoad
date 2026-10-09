using System.Net;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Records only closed, passive facts from the original owner-registration flow.</summary>
internal sealed class PhysicalOwnerRegistrationRf3Evidence
{
    private const int Version = 1;
    private const int MaximumBytes = 4_096;
    private const int FirstNodeOrdinal = 1;
    private const string Kind = "NativeOwnerRegistrationFailure";
    private const string OverBound = "The closed owner-registration diagnostic exceeded its byte bound.";
    private readonly long started = TimeProvider.System.GetTimestamp();
    private PhysicalOwnerRegistrationRf3Phase phase;
    private int nodeOrdinal;
    private bool completedMarker;
    private bool failedMarker;
    private bool responseReceived;
    private int? status;

    internal void Enter(PhysicalOwnerRegistrationRf3Phase value) => phase = value;

    internal void BeginNode(string node)
    {
        nodeOrdinal = Array.IndexOf(TwoRf3MembershipProtocol.Nodes, node) + FirstNodeOrdinal;
        completedMarker = false;
        failedMarker = false;
        responseReceived = false;
        status = null;
    }

    internal void MarkCompleted() => completedMarker = true;
    internal void MarkFailed() => failedMarker = true;
    internal void HealthReceived(HttpStatusCode value)
    { responseReceived = true; status = (int)value; }

    internal void Write(DistributedApplication? app, bool waveReturned, bool applicationDisposed, List<Exception> failures, CancellationToken timeout,
        CancellationToken deadline, CancellationToken caller)
    {
        PhysicalOwnerRegistrationRf3State[]? states = null;
        if (app is not null && !applicationDisposed)
        { ServerFailureObserver.Observe(() => states = ReadStates(app), failures); }
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = Version,
            kind = Kind,
            phase = phase.ToString(),
            nodeOrdinal,
            completedMarker,
            failedMarker,
            responseReceived,
            status,
            applicationReturned = waveReturned,
            applicationAvailable = app is not null,
            applicationDisposed,
            resourceSnapshotCaptured = states is not null,
            timeoutCanceled = timeout.IsCancellationRequested,
            deadlineCanceled = deadline.IsCancellationRequested,
            callerCanceled = caller.IsCancellationRequested,
            elapsedMilliseconds = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds,
            states
        });
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        { throw new InvalidOperationException(OverBound); }
        Console.Error.WriteLine(json);
    }

    private static PhysicalOwnerRegistrationRf3State[] ReadStates(DistributedApplication app)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        return TwoRf3MembershipProtocol.Nodes.Select((node, index) =>
        {
            var present = app.ResourceNotifications.TryGetCurrentState(node, out var actual);
            var resource = model.Resources.Single(candidate => candidate.Name == node);
            return new PhysicalOwnerRegistrationRf3State(index + FirstNodeOrdinal, present,
                actual is not null && ReferenceEquals(resource, actual.Resource), ClosedState(actual?.Snapshot.State?.Text));
        }).ToArray();
    }

    private static PhysicalOwnerRegistrationRf3ResourceState ClosedState(string? state) => state switch
    {
        null => PhysicalOwnerRegistrationRf3ResourceState.NotObserved,
        var value when value == KnownResourceStates.Running => PhysicalOwnerRegistrationRf3ResourceState.Running,
        var value when value == KnownResourceStates.FailedToStart => PhysicalOwnerRegistrationRf3ResourceState.FailedToStart,
        var value when value == KnownResourceStates.Exited => PhysicalOwnerRegistrationRf3ResourceState.Exited,
        var value when value == KnownResourceStates.Finished => PhysicalOwnerRegistrationRf3ResourceState.Finished,
        _ => PhysicalOwnerRegistrationRf3ResourceState.Other
    };
}
