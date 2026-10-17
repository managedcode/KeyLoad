using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeBlobWholeParentFlow
{
    internal static async Task ExecuteAsync(NativeBlobWholeParentOwner owner, CancellationToken token)
    {
        await owner.StartAsync(token).ConfigureAwait(false);
        var wave = owner.Wave;
        var administrator = owner.Administrator;
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var directory = await PhysicalOwnerDirectoryRf3Assertions.ExpectedAsync(wave.Application, wave.Profile, token).ConfigureAwait(false);
        var destination = directory.Owners.Single(entry => entry.Owner.PhysicalShardId != directory.ControlOwner.PhysicalShardId).Owner;
        var seed = new RemoteTransferNativeParentSeed(administrator);
        await seed.CreateAsync(destination.PhysicalShardId, token).ConfigureAwait(false);
        var plan = NativeBlobWireEffectPlan.Create();
        await (wave.nativeBlobWireControls ?? throw new InvalidOperationException("The actual Blob fixture is absent."))
            .ArmAsync(wave, seed, plan, token).ConfigureAwait(false);
        var terminal = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Source.MovePartitionAsync(seed.Request, token).ConfigureAwait(false)).ConfigureAwait(false);
        await NativeBlobWholeParentAssertions.RequireAsync(administrator, seed, terminal, token).ConfigureAwait(false);
        await PartitionMovementNativeBlobEffects.RequireAsync(administrator, seed, token, plan).ConfigureAwait(false);
        var firstOwners = await owner.ReadMembershipAsync(token).ConfigureAwait(false);
        await NativeBlobWholeParentAssertions.RequireEvidenceAsync(wave, firstOwners, token).ConfigureAwait(false);
        var before = await owner.CaptureAsync(firstOwners.Owners, token).ConfigureAwait(false);
        await NativeBlobWholeCold.RestartAsync(wave, administrator, firstOwners.Owners, token).ConfigureAwait(false);
        var secondOwners = await owner.ReadMembershipAsync(token).ConfigureAwait(false);
        await NativeBlobWholeParentAssertions.RequireAsync(administrator, seed, terminal, token).ConfigureAwait(false);
        var after = await owner.CaptureAsync(secondOwners.Owners, token).ConfigureAwait(false);
        await NativeBlobWholeColdAssertions.RequireUnchangedAsync(before, after, token).ConfigureAwait(false);
        await NativeBlobWholeCold.RestartAsync(wave, administrator, secondOwners.Owners, token).ConfigureAwait(false);
        var thirdOwners = await owner.ReadMembershipAsync(token).ConfigureAwait(false);
        await NativeBlobWholeParentAssertions.RequireAsync(administrator, seed, terminal, token).ConfigureAwait(false);
        var final = await owner.CaptureAsync(thirdOwners.Owners, token).ConfigureAwait(false);
        await NativeBlobWholeColdAssertions.RequireUnchangedAsync(after, final, token).ConfigureAwait(false);
    }
}
