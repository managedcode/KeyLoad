using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class QueryReadCutObservationCapture : IQueryReadCutObserver
{
    internal List<QueryReadCutObservation> Values { get; } = [];
    internal Action? AfterCapture
    {
        get; init;
    }
    public void Observe(QueryReadCutObservation observation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AfterCapture?.Invoke();
        Values.Add(observation);
    }

    internal static string Cut(TestDatabase db) => db.Store.Read(view =>
    {
        var page = view.Scan([], 256);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete query fixture cut exceeded its bound.");
        }
        return db.Store.Position + "\n" + string.Join("\n", page.Records.Select(row =>
            Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)));
    });

    internal static async Task RunAsync(Func<TestDatabase, Task> body)
    {
        var failures = new List<Exception>();
        try
        {
            using var db = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => body(db), failures);
        }
        catch (Exception failure) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static PrincipalRecord Reader(TestDatabase db, long epoch, bool revoked = false) =>
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new("reader", "tenant",
            [new("database", "orders", Capability.DocumentsRead | Capability.Query)], [])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        })).Get<PrincipalRecord>();
}
