using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresRf3BackpressureTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, PostgresRf3Selection selection,
        PostgresRf3BackpressureState state, CancellationToken token)
    {
        var controls = selection.Controls ?? throw new InvalidOperationException(PostgresRf3Protocol.InvalidResult);
        var discovery = await Task.WhenAll(Nodes.Select(node => fixture.ReadSnapshotDiscoveryAsync(node, token)));
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var wire = await PostgresRf3WireOwner.OpenAsync(fixture, token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var backend = await PostgresRf3WireAuthentication.AuthenticateAsync(wire, state.Identity, token);
                var arm = controls.WriteArm(state.Identity.Principal.Id, Guid.Empty, GrainReadKind.AstQuery,
                    RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
                var header = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await PostgresRf3WireMessage.WriteAsync(wire.Stream, PostgresRf3BackpressureProtocol.SimpleQuery,
                    Encoding.UTF8.GetBytes(PostgresRf3BackpressureProtocol.Sql + Null), token);
                var original = PostgresRf3InterruptedRow.ReadAsync(wire, header, resume.Task, token);
                var joined = false;
                var operationFailures = new List<Exception>();
                await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    var held = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.AuthorizationReload,
                        RequestCqrsProbeOutcome.Observed, discovery, token);
                    controls.WriteRelease(arm, held.RequestId);
                    var disposed = await controls.WaitForMarkerAsync(arm, RequestCqrsProbePhase.ProducerDisposed,
                        RequestCqrsProbeOutcome.Observed, discovery, token);
                    await SqlRf3Protocol.EqualAsync(held with { Phase = RequestCqrsProbePhase.ProducerDisposed }, disposed);
                    _ = await header.Task.WaitAsync(token);
                    await global::TUnit.Assertions.Assert.That(original.IsCompleted).IsFalse();
                    await PostgresRf3WireAuthentication.CancelAsync(fixture, backend, token);
                    resume.TrySetResult();
                    await original;
                    joined = true;
                }, operationFailures);
                await PostgresRf3BackpressureCleanup.RunAsync(controls, arm, discovery, wire, resume, header.Task, original,
                    joined, operationFailures);
                if (operationFailures.Count != PostgresRf3Protocol.FirstColumn)
                { selection.RetainEvidence(); }
                global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(operationFailures);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private const string Null = "\0";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
}
