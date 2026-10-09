using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns the native client and handler together, including failed construction.</summary>
internal sealed class PartitionMovementLateNativeHttp : IDisposable
{
    private readonly SocketsHttpHandler? handler;
    private readonly HttpClient? client;
    internal HttpClient Client => client ?? throw new InvalidOperationException("The native client was not constructed.");

    internal static PartitionMovementLateNativeHttp Create(Uri endpoint) => new(endpoint);

    private PartitionMovementLateNativeHttp(Uri endpoint)
    {
        var failures = new List<Exception>();
        try
        {
            handler = new SocketsHttpHandler();
            ConfigureHandler(handler);
            client = new HttpClient(handler, disposeHandler: false);
            ConfigureClient(client, endpoint);
        }
        catch (Exception original)
        {
            failures.Add(original);
            try
            { Dispose(); }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static void ConfigureHandler(SocketsHttpHandler owned) => owned.UseProxy = false;
    private static void ConfigureClient(HttpClient owned, Uri endpoint) => owned.BaseAddress = endpoint;

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        { client?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        try
        { handler?.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
