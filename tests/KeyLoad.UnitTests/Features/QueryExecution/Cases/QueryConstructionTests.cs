using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryConstructionTests
{
    private const string DatabaseParameterName = "database";

    [Test]
    public async Task AcCq012NullDatabaseIsRejectedAtConstruction()
    {
        var error = Assert.ThrowsExactly<ArgumentNullException>(() => _ = new QueryEngine(null!, UnitExecutionOptions.QueryExecution()));

        await Assert.That(error.ParamName).IsEqualTo(DatabaseParameterName);
    }
}
