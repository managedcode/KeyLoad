using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MissingMember
{
    private const string KillScenario = "capacity-original-single-member";
    private const string FingerprintProperty = "activeFingerprint";
    private const int HealthJsonDepth = 2;
    private const int SingleKilledOwner = 1;
    private const int SuccessfulKillExitCode = 0;
    private const string RunningState = "running";
    private const string ExitedState = "exited";

    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        TenantPlacementPlanRequest query, PartitionMovementPublicParentRf3NativeCut[] original,
        CancellationToken token)
    {
        await RequireOriginalSixAsync(wave, token);
        var originalRuntime = wave.RemoteRuntime;
        var originalApplication = wave.Application;
        await originalRuntime.KillAsync(TwoRf3MembershipProtocol.Node6, KillScenario, token);
        var killed = originalRuntime.RequireKilledOwners(originalApplication);
        await Assert.That(killed.Count).IsEqualTo(SingleKilledOwner);
        var receipt = killed[TwoRf3MembershipProtocol.Node6];
        await RequireKillAsync(receipt);
        await originalApplication.ResourceNotifications.WaitForResourceAsync(TwoRf3MembershipProtocol.Node1,
            static observed => observed.Snapshot.HealthStatus == HealthStatus.Unhealthy, token);
        using (var http = McpCallerHttp.Create(originalApplication, TwoRf3MembershipProtocol.Node1))
        using (var response = await http.GetAsync(new Uri(http.BaseAddress!, TwoRf3MembershipProtocol.HealthMembership),
            HttpCompletionOption.ResponseHeadersRead, token))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        }
        await RequireRefusalsAsync(seed, query, token);
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireRefusalCutAsync(state, original, stopped, seed.FirstRequest);
        await RequireKillAsync(receipt);
        await TenantCapacityPlacementRf3Cold.RequireDirtyReplayAsync(wave, seed, state, stopped, token);
        await RequireOriginalSixAsync(wave, token);
        await TenantCapacityPlacementRf3Assertions.RequirePolicyReplayAsync(seed, state, token);
    }

    private static async Task RequireOriginalSixAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        using var response = await http.GetAsync(new Uri(http.BaseAddress!, TwoRf3MembershipProtocol.HealthMembership),
            HttpCompletionOption.ResponseHeadersRead, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var bytes = await TwoRf3MembershipFingerprintAssertions.ReadBoundedHealthAsync(response, token);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = HealthJsonDepth });
        var fingerprint = json.RootElement.GetProperty(FingerprintProperty).GetString()
            ?? throw new InvalidDataException(TwoRf3MembershipProtocol.MissingState);
        await TwoRf3MembershipFingerprintAssertions.VerifyHealthAsync(wave.Application, fingerprint, token);
    }

    private static async Task RequireRefusalsAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantPlacementPlanRequest query, CancellationToken token)
    {
        var sdk = await seed.Source.PlanTenantPlacementAsync(query, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.OwnershipLost);
        await Assert.That(sdk.Problem!.Detail).IsEqualTo(TenantPlacementCapacityProtocol.WrongCohort);
        var official = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
        {
            request = query
        }, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.OwnershipLost, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(TenantPlacementCapacityProtocol.WrongCohort);
    }

    private static async Task RequireKillAsync(ContainerRuntimeKillReceipt receipt)
    {
        await Assert.That(receipt.ResourceName).IsEqualTo(TwoRf3MembershipProtocol.Node6);
        await Assert.That(receipt.Before.State).IsEqualTo(RunningState);
        await Assert.That(receipt.Stopped.State).IsEqualTo(ExitedState);
        await Assert.That(receipt.Stopped.Id).IsEqualTo(receipt.Before.Id);
        await Assert.That(receipt.Stopped.ImageId).IsEqualTo(receipt.Before.ImageId);
        await Assert.That(receipt.Stopped.ConfigImage).IsEqualTo(receipt.Before.ConfigImage);
        await Assert.That(receipt.KillExitCode).IsEqualTo(SuccessfulKillExitCode);
        await Assert.That(receipt.KillCompletedAtUtc).IsGreaterThanOrEqualTo(receipt.KillStartedAtUtc);
    }
}
