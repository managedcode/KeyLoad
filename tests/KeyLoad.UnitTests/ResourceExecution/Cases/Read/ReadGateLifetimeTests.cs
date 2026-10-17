using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ReadGateLifetimeTests
{
    private const bool ImmediateDisposal = false;
    private const bool EnteredDisposal = true;
    private const byte ProbeKey = 0x71;
    private const byte ProbeValue = 0x29;
    private const long PositionAdvance = 1;

    [Test]
    [Arguments(ImmediateDisposal)]
    [Arguments(EnteredDisposal)]
    public async Task AcCq016_DisposalObservesRealGateBeforeLaterStoreUse(bool waitForEntry)
    {
        using var database = new TestDatabase();
        var initialPosition = database.Store.Position;
        await using var held = new RealZoneTreeReadGateHold(database.Store);
        if (waitForEntry)
        {
            await held.WaitUntilEnteredAsync();
        }

        await held.DisposeAsync();
        await held.DisposeAsync();
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put([ProbeKey], [ProbeValue]);
            return true;
        });
        await Assert.That(database.Store.Position).IsEqualTo(initialPosition + PositionAdvance);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue([ProbeKey])))
            .IsEquivalentTo(new byte[] { ProbeValue }, CollectionOrdering.Matching);
    }
}
