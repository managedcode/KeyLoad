using KeyLoad.IntegrationTests.Features.ClusterRouting.Assertions;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsAuthorityFaultScenario(bool useMcp, RequestCqrsLifecycleEvidence lifecycle,
    CancellationToken callerToken)
{
    private const string MissingOwner = "The authority-fault wave did not retain its owner.";
    private const string AdminJson = RequestCqrsAuthorityFaultAssertions.AdministratorJson;
    private string root = string.Empty;
    private bool rootCreated;
    private bool waveStartupAttempted;
    private readonly List<Exception> failures = [];
    private string dataRoot = string.Empty;
    private RequestCqrsProbeFixture? controls;
    private RequestCqrsRf3Wave? wave;
    private RequestCqrsRf3Callers? administrator;
    private RequestCqrsRf3Callers? caller;
    private RequestCqrsAuthorityFaultIdentity? identity;
    private IReadOnlyList<ReplicaSiloDiscovery>? discovery;
    private OutboxHead? beforeOutbox;
    private RequestCqrsAuthorityOutcomeOracle? outcomeOracle;
    private RequestCqrsProbeMarkerRecord? heldMarker;
    private CancellationTokenSource? operationDeadline;
    private Task<Result<CommitReceipt>>? sdkCall;
    private Task<RequestCqrsFaultMcpObservation>? mcpCall;
    private Guid armId;
    private Guid commandId;
    private bool originalStarted;

    internal static Task RunAsync(bool officialMcp, CancellationToken cancellationToken)
        => RequestCqrsAuthorityFaultLifecycleRunner.RunAsync(officialMcp, cancellationToken);

    internal List<Exception> Failures => failures;

    internal async Task ExecuteObservedAsync(CancellationToken parentToken)
    {
        try
        { await ExecuteAsync(parentToken).ConfigureAwait(false); }
        catch
        {
            lifecycle.RecordFirstFailure();
            throw;
        }
    }

    internal Task CleanupAsync()
        => RequestCqrsAuthorityFaultCleanup.RunAsync(root, rootCreated, waveStartupAttempted,
            controls, wave, caller, administrator, discovery, operationDeadline, sdkCall, mcpCall,
            armId, originalStarted, outcomeOracle, commandId, failures, lifecycle.RecordOwnerFailure);

    private async Task ExecuteAsync(CancellationToken parentToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.WaveStartup);
        root = RequestCqrsAuthorityFaultProvisioning.NewPrivateRootPath();
        RequestCqrsAuthorityFaultProvisioning.CreatePrivateRoot(root, () => rootCreated = true);
        operationDeadline = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        operationDeadline.CancelAfter(RequestCqrsRf3Protocol.WaveDeadline);
        lifecycle.SetTokens(callerToken, parentToken, operationDeadline.Token);
        await PrepareAsync(operationDeadline.Token).ConfigureAwait(false);
        await RevokeWhileHeldAsync(operationDeadline.Token).ConfigureAwait(false);
        await VerifyAdminAndRevokedCallerAsync(operationDeadline.Token).ConfigureAwait(false);
        await (outcomeOracle ?? throw new InvalidOperationException(MissingOwner)).CaptureAsync(
            (wave ?? throw new InvalidOperationException(MissingOwner)).App, operationDeadline.Token)
            .ConfigureAwait(false);
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.WaveStartup);
        dataRoot = Path.Combine(root, "data");
        var profile = (await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, cancellationToken).ConfigureAwait(false)).Profile;
        var proof = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken).ConfigureAwait(false);
        controls = RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid());
        waveStartupAttempted = true;
        lifecycle.SetStage(RequestCqrsLifecycleStage.WaveStartup);
        wave = await RequestCqrsRf3Wave.StartProbedAsync(dataRoot,
            RequestCqrsAuthorityFaultProvisioning.CurrentImages(proof.Current), controls, cancellationToken,
            lifecycle).ConfigureAwait(false);
        var app = (wave ?? throw new InvalidOperationException(MissingOwner)).App;
        discovery = await RequestCqrsAuthorityFaultDiscovery.ReadAsync(app, profile, cancellationToken).ConfigureAwait(false);
        administrator = await RequestCqrsRf3Callers.ConnectAsync(app, RequestCqrsRf3Protocol.Node1,
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.IdentityCreate);
        identity = await RequestCqrsAuthorityFaultProvisioning.CreateIdentityAsync(
            administrator.Sdk, cancellationToken).ConfigureAwait(false);
        caller = await RequestCqrsRf3Callers.ConnectAsync(app, RequestCqrsRf3Protocol.Node2,
            identity.Secret, cancellationToken).ConfigureAwait(false);
        outcomeOracle = await RequestCqrsAuthorityOutcomeOracle.SeedAsync(dataRoot, profile, identity, caller,
            cancellationToken).ConfigureAwait(false);
        await RequestCqrsAuthorityFaultAssertions.VerifyDocumentAsync(administrator, identity,
            RequestCqrsAuthorityFaultAssertions.InitialJson, 1, cancellationToken).ConfigureAwait(false);
        var status = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(
            identity.Partition, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        beforeOutbox = status.Head;
        await StartHeldWriteAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task StartHeldWriteAsync(CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.HeldWrite);
        var activeIdentity = identity ?? throw new InvalidOperationException(MissingOwner);
        var activeControls = controls ?? throw new InvalidOperationException(MissingOwner);
        commandId = Guid.NewGuid();
        armId = activeControls.WriteArm(activeIdentity.Principal.Id, commandId, null,
            RequestCqrsProbePhase.AuthorizationReload, RequestCqrsProbeAction.Hold);
        var command = RequestCqrsAuthorityFaultProvisioning.Replacement(activeIdentity, commandId,
            RequestCqrsAuthorityFaultAssertions.HeldJson, 1);
        var activeCaller = caller ?? throw new InvalidOperationException(MissingOwner);
        if (useMcp)
        { mcpCall = RequestCqrsFaultCallers.CommitMcpAsync(activeCaller.Mcp, command, cancellationToken); }
        else
        { sdkCall = RequestCqrsFaultCallers.CommitSdkAsync(activeCaller.Sdk, command, cancellationToken); }
        originalStarted = true;
        var marker = await activeControls.WaitForMarkerAsync(armId, RequestCqrsProbePhase.AuthorizationReload,
            RequestCqrsProbeOutcome.Observed, discovery ?? throw new InvalidOperationException(MissingOwner),
            cancellationToken).ConfigureAwait(false);
        heldMarker = marker;
        lifecycle.MarkHeldWriteObserved();
        await RequestCqrsAuthorityFaultAssertions.VerifyHeldMarkerAsync(marker, armId, commandId, discovery)
            .ConfigureAwait(false);
    }

    private async Task RevokeWhileHeldAsync(CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.PersistedRevocation);
        lifecycle.MarkPersistedRevocationEntered();
        var activeIdentity = identity ?? throw new InvalidOperationException(MissingOwner);
        var expected = RequestCqrsAuthorityFaultProvisioning.Revoke(activeIdentity.Principal);
        var saved = await McpCallerAssertions.SdkSuccessAsync(await (administrator
            ?? throw new InvalidOperationException(MissingOwner)).Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await RequestCqrsAuthorityFaultAssertions.VerifyRevocationAckAsync(saved, expected).ConfigureAwait(false);
        identity = activeIdentity with { Principal = saved };
        await VerifyNoEffectAsync(cancellationToken).ConfigureAwait(false);
        var marker = heldMarker ?? throw new InvalidOperationException(MissingOwner);
        (controls ?? throw new InvalidOperationException(MissingOwner)).WriteRelease(armId, marker.RequestId);
        await RequestCqrsAuthorityFaultLifecycleAssertions.VerifyReleasedAndDisposedAsync(
            controls ?? throw new InvalidOperationException(MissingOwner),
            discovery ?? throw new InvalidOperationException(MissingOwner), armId, commandId, marker,
            cancellationToken).ConfigureAwait(false);
        await JoinOriginalAndAssertDeniedAsync().ConfigureAwait(false);
        await (controls ?? throw new InvalidOperationException(MissingOwner)).RetireArmAsync(armId,
            cancellationToken).ConfigureAwait(false);
        await VerifyNoEffectAsync(cancellationToken).ConfigureAwait(false);
    }


    private async Task JoinOriginalAndAssertDeniedAsync()
    {
        if (useMcp)
        {
            var observation = await (mcpCall ?? throw new InvalidOperationException(MissingOwner)).ConfigureAwait(false);
            var requestId = await RequestCqrsAuthorityFaultAssertions.VerifyMcpUnauthorizedAsync(observation)
                .ConfigureAwait(false);
            await Assert.That(requestId).IsEqualTo((heldMarker
                ?? throw new InvalidOperationException(MissingOwner)).RequestId);
            return;
        }
        await RequestCqrsAuthorityFaultAssertions.VerifySdkUnauthorizedAsync(
            await (sdkCall ?? throw new InvalidOperationException(MissingOwner)).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private async Task VerifyAdminAndRevokedCallerAsync(CancellationToken cancellationToken)
    {
        var activeIdentity = identity ?? throw new InvalidOperationException(MissingOwner);
        var activeAdministrator = administrator ?? throw new InvalidOperationException(MissingOwner);
        var command = RequestCqrsAuthorityFaultProvisioning.Replacement(activeIdentity, Guid.NewGuid(), AdminJson, 1);
        var committed = await McpCallerAssertions.SdkSuccessAsync(await activeAdministrator.Sdk.CommitAsync(command,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(committed.CommandId).IsEqualTo(command.CommandId);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await activeAdministrator.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await RequestCqrsAuthorityFaultAssertions.VerifyReceiptAsync(replay.Value, committed).ConfigureAwait(false);
        await RequestCqrsAuthorityFaultAssertions.VerifyDocumentAsync(activeAdministrator, activeIdentity,
            AdminJson, 2, cancellationToken).ConfigureAwait(false);
        await VerifyRevokedCallerRejectedAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyRevokedCallerRejectedAsync(CancellationToken cancellationToken)
    {
        var activeIdentity = identity ?? throw new InvalidOperationException(MissingOwner);
        var reference = new EntityRef(activeIdentity.Partition, RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId);
        var result = await (caller ?? throw new InvalidOperationException(MissingOwner)).Sdk.GetAsync(reference,
            cancellationToken).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.Unauthenticated.ToString());
        await RequestCqrsAuthorityFaultAssertions.VerifyMcpCredentialRejectedAsync(
            caller.Mcp, activeIdentity.Secret,
            reference, cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyNoEffectAsync(CancellationToken cancellationToken)
    {
        var expectedHead = beforeOutbox ?? throw new InvalidOperationException(MissingOwner);
        await RequestCqrsAuthorityFaultAssertions.VerifyDocumentAsync(
            administrator ?? throw new InvalidOperationException(MissingOwner),
            identity ?? throw new InvalidOperationException(MissingOwner),
            RequestCqrsAuthorityFaultAssertions.InitialJson, 1, cancellationToken).ConfigureAwait(false);
        await RequestCqrsAuthorityFaultAssertions.VerifyOutboxUnchangedAsync(
            administrator.Sdk, identity.Partition, expectedHead,
            cancellationToken).ConfigureAwait(false);
    }
}
