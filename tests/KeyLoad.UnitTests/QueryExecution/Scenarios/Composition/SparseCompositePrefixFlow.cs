using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SparseCompositePrefixFlow
{
    internal static async Task RunAsync(bool includeNull, bool includeMissing)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var originals = await SparseCompositePrefixFixture.SeedAsync(fixture, includeNull, includeMissing);
                await SparseCompositePrefixOperations.RunAsync(fixture.Database, fixture.Store, fixture.Partition,
                    includeNull, includeMissing, originals);
                await SparseCompositePrefixFixture.PolicyAsync(fixture.Database, fixture.Partition, false, 2L, originals);
                await DeniedAsync(fixture);
                await SparseCompositePrefixFixture.PolicyAsync(fixture.Database, fixture.Partition, true, 3L, originals);
                await SparseCompositePrefixOperations.RunAsync(fixture.Database, fixture.Store, fixture.Partition,
                    includeNull, includeMissing, originals);
                await SparseCompositePrefixCold.RunAsync(fixture, includeNull, includeMissing, originals);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task DeniedAsync(TestDatabase fixture)
    {
        var engine = new QueryEngine(fixture.Database, UnitExecutionOptions.QueryExecution());
        foreach (var collection in new[] { SparseCompositePrefixData.Only, SparseCompositePrefixData.WithStatusIndex })
        {
            foreach (var execute in new Func<QueryPage>[]
            {
                () => engine.Execute(SparseCompositePrefixData.Reader,
                    SparseCompositePrefixRequests.Sql(fixture.Partition, collection, 0, false)),
                () => engine.ExecuteAst(SparseCompositePrefixData.Reader,
                    SparseCompositePrefixRequests.Ast(fixture.Partition, collection, 0, false)),
                () => engine.ExecuteAst(SparseCompositePrefixData.Reader,
                    SparseCompositePrefixRequests.CSharp(fixture.Partition, collection, 0, false))
            })
            {
                await QueryAdapterNullParityConservation.ObserveAsync(fixture.Store, async () =>
                {
                    var failed = Assert.ThrowsExactly<KeyLoadException>(() => execute());
                    await Assert.That(failed.Code).IsEqualTo(ErrorCode.PermissionDenied);
                    await Assert.That(failed.Message).IsEqualTo("The query requires a protected field-use grant.");
                });
            }
        }
    }
}
