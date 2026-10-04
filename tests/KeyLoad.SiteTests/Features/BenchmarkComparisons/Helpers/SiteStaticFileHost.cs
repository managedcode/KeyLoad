using System.Net;
using System.Net.Sockets;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteStaticFileHost : IAsyncDisposable
{
    private readonly HttpListener listener;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task serving;
    private int disposalStarted;

    private SiteStaticFileHost(HttpListener listener, string root, string baseUrl)
    {
        this.listener = listener;
        Root = root;
        BaseUrl = baseUrl;
        serving = ServeRequests(cancellation.Token);
    }

    public string Root { get; }
    public string BaseUrl { get; }

    public static SiteStaticFileHost Start(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        for (var attempt = SiteTokens.Zero; attempt < SiteTokens.StaticHostStartAttempts; attempt++)
        {
            using var portReservation = new TcpListener(IPAddress.Loopback, SiteTokens.PortZero);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            var baseUrl = $"{SiteTokens.StaticHostAddress}{port}{SiteTokens.UrlPathSeparator}";
            HttpListener? untransferred = new();
            try
            {
                untransferred.Prefixes.Add(baseUrl);
                untransferred.Start();
                var host = new SiteStaticFileHost(untransferred, fullRoot, baseUrl);
                untransferred = null;
                return host;
            }
            catch (HttpListenerException)
            {
                // Retry the bounded native port-binding race after discarding this listener.
                DiscardUnstartedListener(untransferred);
                untransferred = null;
            }
            finally
            {
                untransferred?.Close();
            }
        }

        throw new InvalidOperationException(SiteTokens.StaticHostBindFailure);
    }

    /// <summary>
    /// The managed (non-Windows) listener re-resolves its endpoint while releasing a failed bind and can raise the same
    /// address-in-use failure. The port was never served, so that release failure must not escape the bounded retry.
    /// </summary>
    private static void DiscardUnstartedListener(HttpListener? listener)
    {
        try
        {
            listener?.Abort();
        }
        catch (HttpListenerException)
        {
            // The failed endpoint was never bound for this listener; nothing remains to release.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposalStarted, SiteTokens.One) != SiteTokens.Zero)
        {
            return;
        }

        try
        {
            await cancellation.CancelAsync();
            // Release exactly once: on the managed (non-Windows) listener, Close() after Stop() re-binds the port that
            // Stop() already released, racing parallel tests for it ("Address already in use", website run 37074632196).
            listener.Close();
            using var timeout = new CancellationTokenSource(SiteTokens.StaticHostTimeoutMilliseconds);
            await serving.WaitAsync(timeout.Token);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task ServeRequests(CancellationToken cancellationToken)
    {
        var served = SiteTokens.Zero;
        while (!cancellationToken.IsCancellationRequested && served < SiteBrowserTokens.MaximumHttpRequests)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            served++;
            try
            {
                await Respond(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (IsExpectedClientDisconnect(exception, cancellationToken))
            {
                // A disconnected browser request is local to this response; keep serving later requests.
            }
        }
    }

    private static bool IsExpectedClientDisconnect(Exception exception, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (exception is IOException or HttpListenerException or ObjectDisposedException);

    private async Task Respond(HttpListenerContext context, CancellationToken cancellationToken)
    {
        using var response = context.Response;
        var absolutePath = context.Request.Url!.AbsolutePath;
        if (context.Request.HttpMethod != SiteTokens.StaticGetMethod ||
            absolutePath.Length > SiteTokens.MaximumStaticPathCharacters)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        var requestPath = Uri.UnescapeDataString(absolutePath.TrimStart(SiteTokens.UrlPathSeparatorCharacter));
        var path = Path.GetFullPath(Path.Combine(Root, requestPath));
        if (!IsConfinedRegularFile(path))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        var info = new FileInfo(path);
        if (info.Length > SiteBrowserTokens.MaximumStaticFileBytes)
        {
            response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = ContentType(path);
        response.Headers[SiteBrowserTokens.CacheControlHeader] = SiteBrowserTokens.CacheNoStore;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private static string ContentType(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(SiteBrowserTokens.HtmlExtension, StringComparison.OrdinalIgnoreCase))
        {
            return SiteBrowserTokens.HtmlContentType;
        }
        if (extension.Equals(SiteBrowserTokens.CssExtension, StringComparison.OrdinalIgnoreCase))
        {
            return SiteBrowserTokens.CssContentType;
        }
        if (extension.Equals(SiteBrowserTokens.JavaScriptExtension, StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(SiteBrowserTokens.ModuleExtension, StringComparison.OrdinalIgnoreCase))
        {
            return SiteBrowserTokens.JavaScriptContentType;
        }
        if (extension.Equals(SiteBrowserTokens.JsonExtension, StringComparison.OrdinalIgnoreCase))
        {
            return SiteBrowserTokens.JsonContentType;
        }
        if (extension.Equals(SiteBrowserTokens.SvgExtension, StringComparison.OrdinalIgnoreCase))
        {
            return SiteBrowserTokens.SvgContentType;
        }

        return SiteBrowserTokens.DefaultContentType;
    }

    private bool IsConfinedRegularFile(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootPrefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, comparison) || !File.Exists(path))
        {
            return false;
        }
        var current = path;
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != SiteTokens.Zero)
            {
                return false;
            }
            if (string.Equals(current, Root, comparison))
            {
                return true;
            }
            current = Directory.GetParent(current)?.FullName ?? string.Empty;
            if (current.Length == SiteTokens.Zero)
            {
                return false;
            }
        }
    }
}
