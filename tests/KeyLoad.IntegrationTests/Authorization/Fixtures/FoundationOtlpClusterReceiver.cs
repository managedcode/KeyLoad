using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;
using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed class FoundationOtlpClusterReceiver : IDisposable
{
    private readonly HttpClient client;
    private readonly FoundationOtlpClusterExecutionOptions settings;
    private readonly string browserToken;
    private readonly string apiKey;

    private FoundationOtlpClusterReceiver(HttpClient client, string browserToken, string apiKey, FoundationOtlpClusterExecutionOptions settings)
    {
        this.client = client;
        this.settings = settings;
        this.browserToken = browserToken;
        this.apiKey = apiKey;
    }

    internal static async Task<FoundationOtlpClusterReceiver> ConnectAsync(ClusterFixture fixture,
        IOptions<FoundationOtlpClusterExecutionOptions> options, CancellationToken token)
    {
        var settings = options.Value;
        var (address, browserToken) = await BrowserCredentialsAsync(fixture, settings, token);
        SocketsHttpHandler? pendingHandler = null;
        HttpClient? pendingClient = null;
        FoundationOtlpClusterReceiver? receiver = null;
        var failures = new List<Exception>();
        try
        {
            pendingHandler = new SocketsHttpHandler();
            ConfigureHandler(pendingHandler);
            pendingClient = new HttpClient(pendingHandler, disposeHandler: true);
            pendingHandler = null;
            pendingClient.BaseAddress = new Uri(address.GetLeftPart(UriPartial.Authority));
            pendingClient.Timeout = settings.RequestTimeout;
            var apiKey = await AuthenticateAsync(pendingClient, browserToken, settings, token);
            receiver = new(pendingClient, browserToken, apiKey, settings);
            pendingClient = null;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        finally
        {
            try
            {
                pendingClient?.Dispose();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            try
            {
                pendingHandler?.Dispose();
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return receiver ?? throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
    }

    private static void ConfigureHandler(SocketsHttpHandler handler)
    {
        handler.AllowAutoRedirect = false;
        handler.UseCookies = false;
    }

    private static async Task<(Uri Address, string BrowserToken)> BrowserCredentialsAsync(ClusterFixture fixture,
        FoundationOtlpClusterExecutionOptions settings, CancellationToken token)
    {
        var address = await fixture.App.GetDashboardUrlAsync(token);
        var query = QueryHelpers.ParseQuery(address.Query);
        if (!address.IsLoopback || !query.TryGetValue(FoundationOtlpClusterProtocol.BrowserTokenQuery, out var values) || values.Count != 1
            || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > settings.MaximumPrivateTokenCharacters)
        {
            throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
        }
        return (address, values[0]!);
    }

    private static async Task<string> AuthenticateAsync(HttpClient client, string browserToken,
        FoundationOtlpClusterExecutionOptions settings, CancellationToken token)
    {
        using (var unauthenticated = await client.GetAsync(new Uri(FoundationOtlpClusterProtocol.SpanPath, UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead, token))
        {
            if (unauthenticated.StatusCode != HttpStatusCode.Unauthorized)
            {
                throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
            }
        }
        using var content = JsonContent.Create(new Dictionary<string, string>
        {
            [FoundationOtlpClusterProtocol.BrowserTokenProperty] = browserToken
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, FoundationOtlpClusterProtocol.ExchangePath)
        {
            Content = content
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        using var exchange = await ReadAsync(response, settings, token);
        if (!exchange.RootElement.TryGetProperty(FoundationOtlpClusterProtocol.ApiKeyProperty, out var key) || key.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(key.GetString()) || key.GetString()!.Length > settings.MaximumPrivateTokenCharacters)
        {
            throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
        }
        var apiKey = key.GetString()!;
        client.DefaultRequestHeaders.Add(FoundationOtlpClusterProtocol.ApiKeyHeader, apiKey);
        return apiKey;
    }

    internal async Task<JsonDocument?> ReadSnapshotAsync(string path, string node, string traceId, CancellationToken token)
    {
        var uri = QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
        {
            [FoundationOtlpClusterProtocol.ResourceQuery] = node,
            [FoundationOtlpClusterProtocol.TraceQuery] = traceId,
            [FoundationOtlpClusterProtocol.LimitQuery] = settings.MaximumRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        using var response = await client.GetAsync(new Uri(uri, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        return await ReadAsync(response, settings, token);
    }

    internal string[] Secrets => [browserToken, apiKey];

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response,
        FoundationOtlpClusterExecutionOptions settings, CancellationToken token)
    {
        if (response.StatusCode != HttpStatusCode.OK
            || response.Content.Headers.ContentLength > settings.MaximumResponseBytes)
        {
            throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var chunk = new byte[settings.ReadChunkBytes];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, token);
            if (count == 0)
            {
                break;
            }
            if (count > settings.MaximumResponseBytes - buffer.Length)
            {
                throw new InvalidOperationException(FoundationOtlpClusterProtocol.Failure);
            }
            await buffer.WriteAsync(chunk.AsMemory(0, count), token);
        }
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length)),
            new JsonDocumentOptions { MaxDepth = settings.MaximumJsonDepth });
    }

    public void Dispose() => client.Dispose();
}
