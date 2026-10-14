using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class RemoteGraphNativeProofFlow
{
    internal static async Task RunAsync(Func<TestDatabase, Task> body)
    {
        var failures = new List<Exception>();
        try
        {
            using var database = new TestDatabase();
            await ServerFailureObserver.ObserveAsync(() => body(database), failures);
        }
        catch (Exception error) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static string Image(TestDatabase database) => database.Store.Read(view =>
    {
        var page = view.Scan([], 512);
        if (page.HasMore)
        {
            throw new InvalidOperationException("Native graph fixture exceeded its complete image bound.");
        }
        var identity = database.Store.Identity;
        return identity.NodeId.ToString("D") + ":" + identity.Incarnation.ToString("D") + ":" + database.Store.Position
            + "\\n" + string.Join("\\n", page.Records.Select(record => Convert.ToHexString(record.Key.Span)
                + ":" + Convert.ToHexString(record.Value.Span)));
    });
}
