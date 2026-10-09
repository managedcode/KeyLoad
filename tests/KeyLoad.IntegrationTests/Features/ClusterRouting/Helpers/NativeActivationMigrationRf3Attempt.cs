using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationMigrationRf3Attempt
{
    internal static async Task<RequestCqrsProbeActivationRecord> RunAsync(TwoRf3MembershipWave wave,
        RequestCqrsRf3Callers callers, RequestCqrsPhaseFaultIdentity identity, CommandRequest command,
        RequestCqrsProbeActivationRecord established, CommitReceipt receipt, IReadOnlyList<ReplicaSiloDiscovery> discovery,
        ReplicaSiloDiscovery target, bool cancel, bool foreign, List<Exception> failures, CancellationToken cancellationToken)
    {
        RequestCqrsProbeActivationRecord? observed = null;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var held = new NativeActivationHeldProducer<Result<CommitReceipt>>(wave.QueryControls, discovery,
                identity.PrincipalId, command.CommandId, established.Voter, token => callers.Sdk.CommitAsync(command, token),
                failures, lifetime.Token);
            var witness = await held.ObserveAsync(cancellationToken).ConfigureAwait(false);
            await Assert.That(witness.GrainDigest).IsEqualTo(established.GrainDigest);
            await Assert.That(witness.ActivationId).IsEqualTo(established.ActivationId);
            var request = NativeActivationMigrationRf3Control.Write(wave.QueryControls, witness, target, foreign);
            if (cancel)
            { await lifetime.CancelAsync().ConfigureAwait(false); }
            else
            { held.Release(); }
            var result = await held.Producer.ConfigureAwait(false);
            if (cancel)
            { await RequestCqrsFaultOutcome.AssertSdkUnknownWriteAsync(result).ConfigureAwait(false); }
            else if (foreign)
            {
                await Assert.That(result.IsFailed).IsTrue();
                await Assert.That(result.Value).IsNull();
                await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
                await Assert.That(result.Problem?.Detail).IsEqualTo(GrainRoutingProtocol.InvalidRequest);
            }
            else
            { await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(result)); }
            await NativeActivationMigrationRf3Control.RequireRequestedAsync(wave.QueryControls, request,
                expected: !cancel && !foreign, cancellationToken).ConfigureAwait(false);
            await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(wave.QueryControls, held.Arm, witness.RequestId,
                command.CommandId, discovery, cancellationToken).ConfigureAwait(false);
            if (cancel)
            {
                _ = await wave.QueryControls.WaitForMarkerAsync(held.Arm, RequestCqrsProbePhase.BeforeSubmit,
                    RequestCqrsProbeOutcome.Cancelled, discovery, cancellationToken).ConfigureAwait(false);
                await NativeActivationMigrationRf3Control.RequireRequestedAsync(wave.QueryControls, request,
                    expected: false, cancellationToken).ConfigureAwait(false);
            }
            observed = witness;
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return observed ?? throw new InvalidOperationException(NativeActivationRf3Protocol.Missing);
    }
}
