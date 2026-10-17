using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class MetamorphicReferenceFlow
{
    internal static async Task RunAsync(int seed, bool descending, int offset, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new MetamorphicReferenceFixture(seed);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                var plans = Enumerable.Range(0, 4).Select(shape => MetamorphicReferencePlan.Create(
                    fixture.Harness.Partition, shape, descending, offset)).ToArray();
                await MetamorphicReferenceOperations.DeniedAsync(fixture.Harness.Database, fixture.Harness.Store, plans, token);
                await fixture.PolicyAsync(true, 2L);
                await MetamorphicReferenceOperations.RunAsync(fixture, fixture.Harness.Database, fixture.Harness.Store,
                    plans, descending, offset, token);
                var engine = new QueryEngine(fixture.Harness.Database, UnitExecutionOptions.QueryExecution());
                QueryPage original = null!;
                await MetamorphicReferenceLifetime.ObserveAsync(fixture.Harness.Store, async () =>
                {
                    original = engine.Execute(MetamorphicReferenceOperations.Subject(fixture.Harness.Database), plans[0].Sql,
                        fixture.Harness.Database.EvaluationClock, token);
                    var expected = MetamorphicReferenceData.Reference(fixture.Rows, descending, offset)
                        .Take(MetamorphicReferenceData.PageSize).Select(MetamorphicReferenceData.Expected).ToArray();
                    await MetamorphicReferenceAssertions.PageAsync(original, expected, fixture.Harness.Store.Position,
                        MetamorphicReferenceData.Path(0), false);
                });
                await fixture.PolicyAsync(false, 3L);
                await MetamorphicReferenceOperations.DeniedAsync(fixture.Harness.Database, fixture.Harness.Store, plans, token);
                await fixture.PolicyAsync(true, 4L);
                await MetamorphicReferenceLifetime.ObserveAsync(fixture.Harness.Store, async () =>
                {
                    QueryPage? partial = null;
                    var failure = Assert.ThrowsExactly<KeyLoadException>(() => partial = engine.Execute(
                        MetamorphicReferenceOperations.Subject(fixture.Harness.Database), plans[0].Sql with
                        {
                            Cursor = original.Cursor
                        },
                        fixture.Harness.Database.EvaluationClock, token));
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.CursorExpired);
                    await Assert.That(partial).IsNull();
                });
                await MetamorphicReferenceOperations.RunAsync(fixture, fixture.Harness.Database, fixture.Harness.Store,
                    plans, descending, offset, token);
                await MetamorphicReferenceLifetime.ReplayAsync(fixture, fixture.Harness.Database, fixture.Harness.Store, token);
                await MetamorphicReferenceLifetime.ColdAsync(fixture, plans, descending, offset, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
