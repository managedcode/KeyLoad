using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed class FollowerDocumentRf3HeldFlow(FollowerDocumentRf3State state)
{
    internal async Task CaptureAndHoldAsync(CancellationToken token)
    {
        var administrator = state.Administrator ?? throw new InvalidOperationException(FollowerDocumentRf3Protocol.MissingOwner);
        state.Before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
        await FollowerDocumentRf3Assertions.FollowerStatusAsync(state.Before, state.ReplicaId);
        state.Placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new AtomicPartitionPlacementReadRequest(FollowerDocumentRf3Protocol.Version, state.Identity.Partition), token).ConfigureAwait(false)).ConfigureAwait(false);
        var reference = new EntityRef(state.Identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
        var minimum = new CommitToken(state.Placement.Incarnation, state.Identity.Partition.AtomicPartitionId,
            state.Before.Applied, state.Placement.PlacementEpoch);
        state.Request = new(FollowerDocumentRf3Protocol.Version, reference, state.ReplicaId,
            state.Change == FollowerDocumentChange.Lag ? FollowerDocumentRf3Protocol.ZeroLag : FollowerDocumentRf3Protocol.UnlimitedLag, minimum);
        state.ArmId = state.Controls!.WriteArm(state.Identity.PrincipalId, Guid.Empty, GrainReadKind.FollowerDocument,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        state.CallLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        state.Pending = FollowerDocumentPublicObservation.InvokeAsync(state.Mode, state.Caller!.Sdk, state.Caller.Mcp, state.Request, state.CallLifetime.Token);
        state.Observed = await state.Controls.WaitForMarkerAsync(state.ArmId, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, state.Discovery!, token).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyMarkerAsync(state.Observed, state.ArmId, Guid.Empty,
            RequestCqrsProbePhase.AuthorizationReload, state.Discovery!).ConfigureAwait(false);
        await Assert.That(state.Observed.Voter).IsEqualTo(state.ReplicaId);
        await Assert.That(state.Pending.IsCompleted).IsFalse();
    }

    internal async Task ChangeAndReleaseAsync(CancellationToken token)
    {
        var sdk = state.Administrator!.Sdk;
        if (state.Change == FollowerDocumentChange.Credential)
        { await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureApiKeyAsync(Guid.NewGuid(), state.Credential with { Revoked = true }, token).ConfigureAwait(false)).ConfigureAwait(false); }
        if (state.Change == FollowerDocumentChange.Grant)
        {
            state.Principal = state.Principal with { Grants = [], PolicyEpoch = state.Principal.PolicyEpoch + 1 };
            state.Principal = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Principal, token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        if (state.Change == FollowerDocumentChange.FieldPolicy)
        {
            var resource = new ResourceDefinition(RequestCqrsRf3Protocol.AdminCollection, ResourceKind.Collection,
                state.Identity.Partition.TransactionDomainId)
            {
                SchemaVersion = FollowerDocumentRf3Protocol.SecondRevision,
                FieldPolicies = [new(FollowerDocumentRf3Protocol.ProtectedPath, "private",
                  FollowerDocumentRf3Protocol.FieldGrant, "follower.cohort.use", "follower.cohort.write")]
            };
            await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new ConfigureResourceRequest(state.Identity.Partition.TenantId, state.Identity.Partition.DatabaseId, resource)
                { ExpectedSchemaVersion = FollowerDocumentRf3Protocol.FirstRevision }, token).ConfigureAwait(false)).ConfigureAwait(false);
        }
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(state.Identity, Guid.NewGuid());
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token).ConfigureAwait(false)).ConfigureAwait(false);
        await FollowerDocumentRf3Assertions.FullReceiptAsync(receipt, command, state.Placement);
        await Assert.That(receipt.Token.Position).IsGreaterThan(state.Before.Applied);
        state.Changed = await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
        await FollowerDocumentRf3Assertions.FollowerStatusAsync(state.Changed, state.ReplicaId);
        await Assert.That(state.Changed.Applied).IsGreaterThan(state.Before.Applied);
        await Assert.That(state.Changed.Applied).IsGreaterThanOrEqualTo(receipt.Token.Position);
        await Assert.That(state.Changed.ConsensusTerm).IsEqualTo(state.Before.ConsensusTerm);
        if (state.Change == FollowerDocumentChange.Cancellation)
        { await state.CallLifetime!.CancelAsync().ConfigureAwait(false); return; }
        if (state.Change == FollowerDocumentChange.NoQuorum)
        { await new FollowerDocumentRf3QuorumFlow(state).RemoveOtherVotersAsync(token).ConfigureAwait(false); }
        state.Controls!.WriteRelease(state.ArmId, state.Observed.RequestId);
    }

    internal async Task AssertOriginalAndContinueAsync(CancellationToken token)
    {
        var original = await state.Pending!.ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(state.Controls!, state.ArmId, state.Observed.RequestId,
            Guid.Empty, state.Discovery!, token).ConfigureAwait(false);
        await state.Controls!.RetireArmAsync(state.ArmId, token).ConfigureAwait(false);
        if (state.Change is FollowerDocumentChange.Credential or FollowerDocumentChange.Grant or FollowerDocumentChange.Lag or FollowerDocumentChange.NoQuorum)
        {
            var code = RefusalCode(state.Change);
            await FollowerDocumentRf3Assertions.RejectedAsync(original, code, state.Identity.Secret);
        }
        else if (state.Change == FollowerDocumentChange.Cancellation)
        { await FollowerDocumentRf3Assertions.CancelledAsync(original, state.Identity.Secret, state.CallLifetime!.Token); }
        else
        {
            var redacted = state.Change == FollowerDocumentChange.FieldPolicy;
            await FollowerDocumentRf3Assertions.FullAsync(original, state.Request, state.Placement, state.Before, state.Changed,
                redacted ? FollowerDocumentRf3Protocol.RedactedOld : RequestCqrsRf3Protocol.DocumentJson,
                FollowerDocumentRf3Protocol.FirstRevision, state.Principal.PolicyEpoch, redacted);
        }
        if (state.Change == FollowerDocumentChange.NoQuorum)
        { await new FollowerDocumentRf3QuorumFlow(state).RestoreVotersAndVerifyAsync(token).ConfigureAwait(false); }
        else
        { await RestoreAndContinueAsync(token).ConfigureAwait(false); }
    }

    private static ErrorCode RefusalCode(FollowerDocumentChange change) => change switch
    {
        FollowerDocumentChange.Credential => ErrorCode.Unauthenticated,
        FollowerDocumentChange.Grant => ErrorCode.PermissionDenied,
        FollowerDocumentChange.NoQuorum => ErrorCode.OwnershipLost,
        _ => ErrorCode.HistoryUnavailable
    };

    private async Task RestoreAndContinueAsync(CancellationToken token)
    {
        var sdk = state.Administrator!.Sdk;
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureApiKeyAsync(Guid.NewGuid(), state.Credential, token).ConfigureAwait(false)).ConfigureAwait(false);
        state.Principal = new(state.Identity.PrincipalId, state.Identity.Partition.TenantId,
            [new(state.Identity.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], [FollowerDocumentRf3Protocol.FieldGrant])
        { ClusterAdministrator = false, PolicyEpoch = state.Principal.PolicyEpoch + 1 };
        state.Principal = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigurePrincipalAsync(Guid.NewGuid(), state.Principal, token).ConfigureAwait(false)).ConfigureAwait(false);
        await new FollowerDocumentRf3Continuation(state).VerifyHealthyAsync(sdk, state.Caller!, state.ReplicaId, token).ConfigureAwait(false);
    }

}
