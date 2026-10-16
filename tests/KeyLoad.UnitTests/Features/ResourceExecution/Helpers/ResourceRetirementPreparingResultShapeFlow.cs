using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementPreparingResultShapeFlow
{
    internal static Task RunAsync() => ResourceRetirementPreparationValidationFixture.RunAsync(async (state, token) =>
    {
        var seed = ResourceRetirementPreparingShapeData.Seed(state);
        var limits = seed.Source.Database.Limits;
        ResourceRetirementPhaseReplyValidation.Require(seed.Phase, seed.Result, limits);
        var recorded = ResourceRetirementPreparingShapeData.At(seed.Source, seed.Source.Journal.Log.State.LastIndex);
        await Assert.That(recorded.Phase.Phase).IsEqualTo(ResourceRetirementPhase.RecordReservation);
        ResourceRetirementPhaseReplyValidation.Require(recorded.Phase, recorded.Result, limits);
        foreach (var pair in new[]
        {
            (seed.Phase, seed.Result with { OwnerReservation = seed.Reservation }),
            (recorded.Phase, recorded.Result with { OwnerReservation = seed.Reservation }),
            (seed.Phase, seed.Result with { PreparationState = null, OwnerReservation = seed.Reservation }),
            (recorded.Phase, recorded.Result with { PreparationState = null }),
            (seed.Phase with { Body = NativeSerialization.Serialize(new ResourceRetirementPrepareBodyV1(
                seed.Request with { RetirementId = Guid.NewGuid() })) }, seed.Result)
        })
        {
            await ResourceRetirementPreparingShapeData.RefusalAsync(() => ResourceRetirementPhaseReplyValidation.Require(
                ResourceRetirementPreparingShapeData.RoundTrip(pair.Item1), ResourceRetirementPreparingShapeData.RoundTrip(pair.Item2), limits),
                ErrorCode.Corruption);
        }
        token.ThrowIfCancellationRequested();
        ResourceRetirementPhaseReplyValidation.Require(recorded.Phase, recorded.Result, limits);
    });
}
