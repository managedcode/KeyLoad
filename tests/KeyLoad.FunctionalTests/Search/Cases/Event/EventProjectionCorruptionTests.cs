
namespace KeyLoad.FunctionalTests.Search;

internal sealed class EventProjectionCorruptionTests
{
    [Test]
    public async Task AcLineage002MalformedPersistedLineageFailsClosedInsteadOfLeakingTheVector()
    {
        await ClassificationLineageCorruptionFlow.RunAsync();
    }
}
