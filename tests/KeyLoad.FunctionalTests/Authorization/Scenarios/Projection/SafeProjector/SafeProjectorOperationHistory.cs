namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class SafeProjectorOperationHistory(SafeProjectorOperationState state)
{
    internal SafeProjectorOperationState State { get; } = state;
    internal List<SafeProjectorOperationCommand> Commands
    {
        get;
    } =
        [state.ResourceCommand, state.PrincipalCommand, state.Seed];
    internal SafeProjectorOperationCommand? PublicPatch
    {
        get; set;
    }
    internal SafeProjectorOperationCommand? HealthyPatch
    {
        get; set;
    }

    internal async Task SetAsync(SafeProjectorOperationDatabase owner, long epoch,
        System.Collections.Immutable.ImmutableArray<string> grants)
    {
        Commands.Add(await SafeProjectorOperationAuthority.SetAsync(owner, epoch, grants));
        await CurrentAsync(owner, epoch, grants);
    }

    internal async Task CurrentAsync(SafeProjectorOperationDatabase owner, long epoch,
        System.Collections.Immutable.ImmutableArray<string> grants)
    {
        await SafeProjectorOperationCanonical.CurrentAsync(owner, State,
            SafeProjectorOperationSeed.Principal(owner, epoch, grants), PublicPatch, HealthyPatch);
        await SafeProjectorOperationOutbox.CurrentAsync(owner, State, PublicPatch, HealthyPatch);
    }
}
