using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed class FollowerDocumentRf3QuorumFlow(FollowerDocumentRf3State state)
{
    internal async Task RemoveOtherVotersAsync(CancellationToken token)
    {
        for (var index = 0; index < RequestCqrsRf3Protocol.NodeCount; index++)
        {
            if (index != state.SelectedIndex)
            {
                state.Stage = FollowerDocumentRf3FailureStage.KillingOtherVoter;
                await state.Wave!.KillDueNoQuorumAsync(RequestCqrsRf3Protocol.NodeName(index), token).ConfigureAwait(false);
            }
        }
    }

    internal async Task RestoreVotersAndVerifyAsync(CancellationToken token)
    {
        for (var index = 0; index < RequestCqrsRf3Protocol.NodeCount; index++)
        {
            if (index != state.SelectedIndex)
            {
                state.Stage = FollowerDocumentRf3FailureStage.RestartingOtherVoter;
                await state.Wave!.RestartAsync(RequestCqrsRf3Protocol.NodeName(index), token).ConfigureAwait(false);
            }
        }
        for (var index = 0; index < RequestCqrsRf3Protocol.NodeCount; index++)
        {
            state.Stage = FollowerDocumentRf3FailureStage.WaitingRestoredHealth;
            await state.Wave!.App.ResourceNotifications.WaitForResourceHealthyAsync(RequestCqrsRf3Protocol.NodeName(index), token)
                .ConfigureAwait(false);
        }
        state.Stage = FollowerDocumentRf3FailureStage.ReadingRestoredStatus;
        var status = await McpCallerAssertions.SdkSuccessAsync(await state.Administrator!.Sdk.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false);
        var indexOfFollower = -1;
        var follower = string.Empty;
        for (var index = 0; index < RequestCqrsRf3Protocol.NodeCount; index++)
        {
            state.Stage = FollowerDocumentRf3FailureStage.ReadingRestoredDiscovery;
            var current = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(state.Wave!.App,
                RequestCqrsRf3Protocol.NodeName(index), state.Profile, token).ConfigureAwait(false);
            if (indexOfFollower < 0 && current.VoterId != status.Leader)
            { indexOfFollower = index; follower = current.VoterId; }
        }
        if (indexOfFollower < 0)
        { throw new InvalidOperationException(FollowerDocumentRf3Protocol.MissingOwner); }
        await VerifyResumedOwnersAsync(RequestCqrsRf3Protocol.NodeName(indexOfFollower), follower, token).ConfigureAwait(false);
    }

    private async Task VerifyResumedOwnersAsync(string node, string follower, CancellationToken token)
    {
        RequestCqrsRf3Callers? resumedAdmin = null;
        RequestCqrsRf3Callers? resumedCaller = null;
        var resumeFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                state.Stage = FollowerDocumentRf3FailureStage.ConnectingRestoredAdministrator;
                resumedAdmin = await RequestCqrsRf3Callers.ConnectAsync(state.Wave!.App, node, state.Profile.AdminKey, token).ConfigureAwait(false);
                state.Stage = FollowerDocumentRf3FailureStage.ConnectingRestoredCaller;
                resumedCaller = await RequestCqrsRf3Callers.ConnectAsync(state.Wave.App, node, state.Identity.Secret, token).ConfigureAwait(false);
                state.Stage = FollowerDocumentRf3FailureStage.VerifyingRestoredHealthy;
                await new FollowerDocumentRf3Continuation(state).VerifyHealthyAsync(resumedAdmin.Sdk, resumedCaller, follower, token).ConfigureAwait(false);
            }
            catch (Exception original)
            {
                FollowerDocumentRf3FailureObservation.Write(original, state, token);
                throw;
            }
        }, resumeFailures).ConfigureAwait(false);
        await RequestCqrsPhaseFaultCleanup.RunAsync(string.Empty, false, null, null, false,
            resumedCaller, resumedAdmin, null, null, null, null, null, Guid.Empty, resumeFailures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(resumeFailures);
    }
}
