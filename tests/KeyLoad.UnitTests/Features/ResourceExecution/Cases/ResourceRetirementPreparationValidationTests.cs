using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class ResourceRetirementPreparationValidationTests
{
    [Test]
    public Task ActualNativeStatusValuesRejectIncompleteProofsAndAllowBusinessPositionVariation()
        => ResourceRetirementPreparationValidationFixture.RunAsync(async (state, token) =>
        {
            ResourceRetirementPreparationStatuses.RequireReady(state.Statuses, state.Preparation);
            await ResourceRetirementPreparationValidationAssertions.RejectAsync(state);
            var advanced = await ResourceRetirementPreparationValidationBusiness.AdvanceAsync(state, token);
            ResourceRetirementPreparationStatuses.RequireReady(advanced, state.Preparation);
        });
}
