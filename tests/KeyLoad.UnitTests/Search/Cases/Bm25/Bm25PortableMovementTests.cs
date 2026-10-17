namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25PortableMovementTests
{
    [Test]
    public Task ActualServerPublicationMovesExactNativeHeadAndStrictValidatorsRefuseWithoutEffects()
        => Bm25PortableMovementTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
