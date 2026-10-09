using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeActivationRf3Scenario
{
    private const int EmptyFailureCount = 0;
    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        RequestCqrsRf3Callers? administrator = null;
        RequestCqrsRf3Callers? callers = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(cancellationToken).ConfigureAwait(false);
            await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, cancellationToken).ConfigureAwait(false);
            administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                wave.Profile.AdminKey, cancellationToken).ConfigureAwait(false);
            _ = await ProtectedDocumentRf3Seed.CreateAsync(wave, administrator.Sdk, cancellationToken).ConfigureAwait(false);
            var identity = await RequestCqrsPhaseFaultProvisioning.CreatePersistedIdentityAsync(administrator.Sdk,
                cancellationToken).ConfigureAwait(false);
            callers = await RequestCqrsRf3Callers.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                identity.Secret, cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(wave, administrator, callers, identity, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (callers is { } originalCallers)
        { await ServerFailureObserver.ObserveAsync(() => originalCallers.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (administrator is { } admin)
        { await ServerFailureObserver.ObserveAsync(() => admin.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is { } owners)
        {
            if (failures.Count != EmptyFailureCount)
            { owners.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers callers, RequestCqrsPhaseFaultIdentity identity, CancellationToken cancellationToken)
    {
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(identity, Guid.NewGuid());
        var discovery = await DiscoverAsync(wave, cancellationToken).ConfigureAwait(false);
        var first = await NativeActivationRf3HeldCall.ExecuteAsync(wave, callers, identity, command,
            useMcp: false, discovery, cancellationToken).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(callers, identity,
            RequestCqrsRf3Protocol.ChangedDocumentJson, NativeActivationRf3Protocol.CommittedRevision, cancellationToken).ConfigureAwait(false);
        var node = RequestCqrsProbeFixtureProtocol.Nodes.Single(name => RequestCqrsProbeFileNames.OriginForNode(name) == first.Witness.Voter);
        await wave.RemoteRuntime.KillAsync(node, NativeActivationRf3Protocol.Replacement, cancellationToken).ConfigureAwait(false);
        await wave.RemoteRuntime.RestartAsync(node, cancellationToken).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(cancellationToken).ConfigureAwait(false);
        discovery = await DiscoverAsync(wave, cancellationToken).ConfigureAwait(false);
        var second = await NativeActivationRf3HeldCall.ExecuteAsync(wave, callers, identity, command,
            useMcp: true, discovery, cancellationToken).ConfigureAwait(false);
        await NativeActivationRf3Witness.RequireReplacementAsync(first.Witness, second.Witness).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(first.Receipt, second.Receipt).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(callers, identity,
            RequestCqrsRf3Protocol.ChangedDocumentJson, NativeActivationRf3Protocol.CommittedRevision, cancellationToken).ConfigureAwait(false);
        await NativeActivationRf3ColdOutcome.VerifyAsync(wave, identity, command, first.Receipt,
            cancellationToken).ConfigureAwait(false);
        var reference = new EntityRef(identity.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId);
        await RequestCqrsFaultReplayContinuation.VerifyAsync(callers, administrator.Sdk, reference, command,
            first.Receipt, RequestCqrsRf3Protocol.DocumentJson, NativeActivationRf3Protocol.CommittedRevision,
            RequestCqrsRf3Protocol.ChangedDocumentJson, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ReplicaSiloDiscovery>> DiscoverAsync(TwoRf3MembershipWave wave,
        CancellationToken cancellationToken)
    {
        var result = new List<ReplicaSiloDiscovery>();
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            result.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application, node, wave.Profile,
            cancellationToken).ConfigureAwait(false));
        }
        return result;
    }
}
