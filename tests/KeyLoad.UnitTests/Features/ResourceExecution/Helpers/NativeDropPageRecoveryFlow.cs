using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageRecoveryFlow
{
    internal static async Task RunAsync(Func<NativeDropPageAttemptState, Task> scenario, bool unknown)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var failures = new List<Exception>();
        var expectedOriginals = new List<Exception>();
        NativeDropRuntimeProfile? profile = null;
        NativeDropPageAttemptState? state = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            profile = await NativeDropRuntimeProfile.CreateAsync(token).ConfigureAwait(false);
            state = await NativeDropPageRecoverySetup.OpenAsync(profile, token).ConfigureAwait(false);
            await scenario(state).ConfigureAwait(false);
            if (!unknown)
            {
                await CompleteAsync(state).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (profile is not null)
        {
            profile.Observer.ReleaseAll();
            if (state is not null)
            {
                await JoinExpectedAsync(state, failures, expectedOriginals).ConfigureAwait(false);
            }

            if (failures.Count != NativeDropRuntimeProtocol.NoFailures)
            {
                profile.RetainRoots();
            }

            await ServerFailureObserver.ObserveAsync(() => profile.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        foreach (var failure in failures)
        {
            failure.Data[NativeDropPageRecoveryData.ExpectedOriginalFailuresKey] = expectedOriginals.ToArray();
        }

        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task JoinExpectedAsync(NativeDropPageAttemptState state, List<Exception> failures,
        List<Exception> expected)
    {
        foreach (var item in state.ExpectedOriginals)
        {
            var observed = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => item.Original, observed).ConfigureAwait(false);
            foreach (var error in observed)
            {
                expected.Add(error);
                if (!ReferenceEquals(error, item.ExpectedFailure))
                {
                    failures.Add(error);
                }
            }
        }
    }

    private static async Task RequireReplacementAsync(NativeDropPageAttemptState state)
    {
        if (state.ReplacedAttempt is not { } original)
        {
            return;
        }

        var checkpoint = state.Profile.Source.Database.ReadResourceRetirementCleanupCheckpoint(
            PartitionStoreProtocol.AdministratorId, state.OriginalState.Control, state.Scope.Page!.Partition,
            NativeDropPreparedPageProducer.Work(state.Profile, state.Context.Token));
        await Assert.That(checkpoint).IsNotNull();
        await Assert.That(checkpoint!.AttemptId).IsNotEqualTo(original);
    }

    private static async Task CompleteAsync(NativeDropPageAttemptState state)
    {
        state.Profile.Observer.ReleaseAll();
        var result = NativeDropRuntimeAssertions.Value<DropTableResult>(await state.Call.Original.ConfigureAwait(false));
        await state.Profile.Observer.JoinSelectedAsync(state.Context.Token).ConfigureAwait(false);
        await RequireReplacementAsync(state).ConfigureAwait(false);
        for (var cold = default(int); cold < NativeDropRuntimeProtocol.ColdReopens; cold++)
        {
            await state.Profile.RestartAsync(state.Context.Token).ConfigureAwait(false);
            var replay = NativeDropPublicSubmission.Start(state.Profile, state.Lookup.Request,
                state.Lookup.CommandId, state.Context.Token);
            await NativeDropRuntimeAssertions.SameAsync(result,
                NativeDropRuntimeAssertions.Value<DropTableResult>(await replay.Original.ConfigureAwait(false))).ConfigureAwait(false);
            await RequireReplacementAsync(state).ConfigureAwait(false);
            await NativeDropPageRecoverySetup.HealthyOtherAsync(state, state.Context.Token).ConfigureAwait(false);
        }
    }
}
