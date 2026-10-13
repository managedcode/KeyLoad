using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPackingActivationRf3Scenario
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        RequestCqrsRf3Callers? administrator = null;
        RequestCqrsRf3Callers? first = null;
        RequestCqrsRf3Callers? second = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(token);
            await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
            administrator = await RequestCqrsRf3Callers.ConnectAsync(wave.Application,
                TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, token);
            var suffix = Guid.NewGuid().ToString("N");
            var firstIdentity = await TenantPackingActivationRf3Provisioning.CreateAsync(
                administrator.Sdk, "packed-first-" + suffix, token);
            var secondIdentity = await TenantPackingActivationRf3Provisioning.CreateAsync(
                administrator.Sdk, "packed-second-" + suffix, token);
            first = await RequestCqrsRf3Callers.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                firstIdentity.Secret, token);
            second = await RequestCqrsRf3Callers.ConnectAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
                secondIdentity.Secret, token);
            var placements = await TenantPackingActivationRf3Admission.BindAsync(administrator, first,
                firstIdentity, secondIdentity, token);
            var sharedId = Guid.NewGuid();
            var firstSeed = TenantPackingActivationRf3Provisioning.Seed(firstIdentity, sharedId);
            var secondSeed = TenantPackingActivationRf3Provisioning.Seed(secondIdentity, sharedId);
            var firstReceipt = await TenantPackingActivationRf3Provisioning.CommitAsync(first, firstSeed, token);
            var secondReceipt = await TenantPackingActivationRf3Provisioning.CommitAsync(second, secondSeed, token);
            var state = new TenantPackingActivationRf3State(firstIdentity, secondIdentity, firstSeed, secondSeed,
                firstReceipt, secondReceipt, placements.First, placements.Second);
            await TenantPackingActivationRf3Flow.RunAsync(wave, administrator, first, second, state, token);
        }, failures);
        if (second is { } ownedSecond)
        { await ServerFailureObserver.ObserveAsync(() => ownedSecond.DisposeAsync().AsTask(), failures); }
        if (first is { } ownedFirst)
        { await ServerFailureObserver.ObserveAsync(() => ownedFirst.DisposeAsync().AsTask(), failures); }
        if (administrator is { } ownedAdministrator)
        { await ServerFailureObserver.ObserveAsync(() => ownedAdministrator.DisposeAsync().AsTask(), failures); }
        if (wave is { } owners)
        {
            if (failures.Count != 0)
            { owners.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
