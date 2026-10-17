
namespace KeyLoad.UnitTests.Features.Search;

internal sealed class EventProjectionCorruptionTests
{
    [Test]
    public async Task AcLineage002MalformedPersistedLineageFailsClosedInsteadOfLeakingTheVector()
    {
        await ClassificationLineageCorruptionFlow.RunAsync();
    }
}
