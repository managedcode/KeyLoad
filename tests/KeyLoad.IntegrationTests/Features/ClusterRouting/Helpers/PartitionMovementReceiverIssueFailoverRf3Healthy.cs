using KeyLoad.IntegrationTests.Features.ClientApi;
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using Microsoft.Extensions.DependencyInjection;
using KeyLoad.Server.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Node5 actual read proof precedes unchanged node4 dispatch, full models and exact cold public replay.</summary>
internal static class PartitionMovementReceiverIssueFailoverRf3Healthy
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        Result<PartitionMoveResult> result, CancellationToken cancellationToken)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(result);
        await PartitionMovementPublicParentRf3Scenario.RequireTerminalAsync(seed, seed.FirstRequest,
            seed.OriginalPlacement, seed.Directory.ControlOwner, actual, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var node5 = await ReadNode5Async(wave, cancellationToken).ConfigureAwait(false);
        var originalId = PartitionMovementReceiverIssueFailoverRf3NativeCut.StageId(seed);
        var before = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            originalId, cancellationToken).ConfigureAwait(false);
        var original = before.First(cut => cut.OriginalPhase is not null).OriginalPhase!;
        await Assert.That(original.OriginalResult!.Error).IsNull();
        await Assert.That(original.ObservationCheckpointReceipt).IsNotNull();
        await PartitionMovementReceiverIssueFailoverRf3ProofAssertions.RequireAsync(before, original, node5);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(before).ConfigureAwait(false);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
        await PartitionMovementPublicCallerReplay.RequireAsync(seed.Source, seed.Official, seed.FirstRequest,
            actual, cancellationToken).ConfigureAwait(false);
        await seed.VerifyAsync(cancellationToken).ConfigureAwait(false);
        var after = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest,
            originalId, cancellationToken).ConfigureAwait(false);
        await PartitionMovementParentOperationalCapacityRf3Assertions.RequireNoEffectsAsync(wave, before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, cancellationToken).ConfigureAwait(false);
    }
    private static async Task<ReplicaSiloDiscovery> ReadNode5Async(TwoRf3MembershipWave wave,
        CancellationToken cancellationToken)
    {
        var model = wave.Application.Services.GetRequiredService<DistributedApplicationModel>();
        var incarnation = await model.Resources.OfType<ParameterResource>()
            .Single(resource => resource.Name == ReceiverIncarnationParameter).GetValueAsync(cancellationToken).ConfigureAwait(false);
        if (!Guid.TryParseExact(incarnation, ReceiverIncarnationFormat, out var actualIncarnation) || actualIncarnation == Guid.Empty)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        var secret = Convert.FromBase64String(wave.MovementTargetPeerKey
            ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState));
        try
        {
            var source = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                TwoRf3MembershipProtocol.Node1, wave.Profile, cancellationToken).ConfigureAwait(false);
            return await TwoRf3MembershipSignedDiscovery.ReadAsync(wave.Application, TwoRf3MembershipProtocol.Node5,
                source.ClusterId, actualIncarnation, secret, cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    private const string ReceiverIncarnationFormat = "D";
    private const string ReceiverIncarnationParameter = "membership-incarnation-b";

}
