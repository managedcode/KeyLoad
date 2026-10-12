using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.DocumentStorage;

using KeyLoad.Client;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferNativeReplayTrial : IAsyncDisposable
{
    private readonly List<Exception> failures;
    private readonly RemoteModelNativeReplayVariation mode;
    private TwoRf3MembershipWave? wave;
    private RemoteTransferNativeAdministrator? administrator;
    private RemoteTransferNativeCallers? calls;
    private Guid? armId;
    private Task? producer;
    private Task? disposal;

    private RemoteTransferNativeReplayTrial(List<Exception> failures, RemoteModelNativeReplayVariation mode)
    { this.failures = failures; this.mode = mode; }

    internal static async Task RunAsync(RemoteModelNativeReplayVariation mode, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        await using (var trial = new RemoteTransferNativeReplayTrial(failures, mode))
        {
            try
            {
                var original = trial.ExecuteAsync(lifetime.Token);
                trial.producer = original;
                await ServerFailureObserver.ObserveAsync(() => original, failures);
            }
            finally { ServerFailureObserver.Observe(lifetime.Cancel, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task ExecuteAsync(CancellationToken token)
    {
        wave = await NativeCanonicalModelCutWaveStartup.StartAsync(RemoteTransferDistinctProtocol.TechnicalSubject, token);
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token);
        var destination = directory.Owners.Single(entry => entry.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner;
        administrator = new();
        await administrator.InitializeAsync(wave, token);
        var parent = new RemoteTransferNativeParentSeed(administrator);
        await parent.CreateAsync(destination.PhysicalShardId, token);
        var setup = await RemoteTransferNativeSetup.CreateAsync(administrator, parent, token);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(await administrator.Source.MovePartitionAsync(parent.Request, token));
        await Assert.That(terminal.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await parent.RequireAsync(token);
        calls = await RemoteTransferNativeCallers.ConnectAsync(wave, TwoRf3MembershipProtocol.Nodes[0], setup.Seed.Identity.Secret, token);
        var created = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(setup.Seed.Create, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(created, setup.Seed.Create, setup.Seed.Scenario.SourceQueue,
            setup.Seed.TransferId, RemoteTransferColdProtocol.CreateKind, RemoteTransferColdProtocol.CreateRevision);
        await calls.ReplayAsync(setup.Seed.Create, created, token);
        var intent = await PublicReadAssertions.TransferAsync(await calls.Sdk.InspectQueueTransferAsync(setup.Seed.SourceRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        await Assert.That(intent.State).IsEqualTo(QueueTransferState.OutputPending);
        await Assert.That(intent.ReceiptToken).IsNull();
        await RemoteTransferNativeStateAssertions.StateAsync(calls, setup.Seed, intent, null, null, token);
        var accept = setup.Seed.Accept(intent) with { CommandId = Guid.NewGuid() };
        var hold = new RemoteTransferNativeReplayHold(wave.QueryControls, setup.Seed.Identity.Principal.Id, accept, mode);
        armId = hold.ArmId;
        var held = await RemoteTransferNativeReplayOperation.ExecuteAsync(wave, calls, setup.Seed, accept, hold, mode, token);
        await calls.DisposeAsync();
        calls = null;
        await RemoteTransferNativeAuthorizedContinuation.ExecuteAsync(wave, administrator, setup.Seed, intent, accept,
            held.Scope, held.Request, held.Committed, created, token);
        var before = await RemoteTransferNativeCutLifecycle.LiveAsync(wave, held.Scope, token);
        await administrator.RequireAsync(parent.Partition, McpToolNames.AdminPartitionMove, parent.Request,
            ct => administrator.Source.MovePartitionAsync(parent.Request, ct), terminal, token);
        await parent.RequireAsync(token);
        RemoteTransferNativeCutLifecycle.RequireUnchanged(before,
            await RemoteTransferNativeCutLifecycle.LiveAsync(wave, held.Scope, token), token);
        using var finalOwners = await RemoteTransferNativeMembership.ReadAsync(wave, token);
        await NativeCanonicalModelCutAdmissionAssertions.RequireRetiredOriginalAsync(wave, wave.Profile.AdminKey,
            held.Scope, finalOwners.Owners, finalOwners.Secrets, token);
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        var cleanup = new List<Exception>();
        if (producer is { } original)
        { await ServerFailureObserver.ObserveAsync(() => original, cleanup); }
        if (calls is { } logical)
        { await ServerFailureObserver.ObserveAsync(() => logical.DisposeAsync().AsTask(), cleanup); }
        if (administrator is not null)
        {
            try
            { await administrator.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { cleanup.Add(failure); }
            catch (Exception failure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { cleanup.Add(failure); }
        }
        if (wave is { } actual)
        {
            if (armId is { } arm && !actual.QueryControls.ArmFor(arm).Retired)
            {
                using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
                await ServerFailureObserver.ObserveAsync(() => actual.QueryControls.RetireArmAsync(arm, deadline.Token), cleanup);
            }
            if (failures.Count != 0 || cleanup.Count != 0) { actual.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => actual.DisposeAsync().AsTask(), cleanup);
        }
        failures.AddRange(cleanup);
    }
}
