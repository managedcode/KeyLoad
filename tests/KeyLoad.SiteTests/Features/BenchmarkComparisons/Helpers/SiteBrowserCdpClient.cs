using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteBrowserCdpClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim sendGate = new(SiteBrowserTokens.One, SiteBrowserTokens.One);
    private readonly ConcurrentQueue<JsonElement> events = new();
    private int nextCommandId;

    private SiteBrowserCdpClient() { }

    public static async Task<SiteBrowserCdpClient> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var client = new SiteBrowserCdpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds);
            await client.socket.ConnectAsync(endpoint, timeout.Token);
            return client;
        }
        catch (OperationCanceledException)
        {
            await DisposeAfterConnectionFailure(client);
            throw;
        }
        catch (WebSocketException)
        {
            await DisposeAfterConnectionFailure(client);
            throw;
        }
    }

    public async Task<JsonElement> CommandAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var commandId = Interlocked.Increment(ref nextCommandId);
        var message = JsonSerializer.SerializeToUtf8Bytes(
            new CdpCommand(commandId, method, parameters ?? new Dictionary<string, object?>()), JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds);
        await sendGate.WaitAsync(timeout.Token);
        try
        {
            await socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, timeout.Token);
        }
        finally
        {
            sendGate.Release();
        }

        while (true)
        {
            using var document = await ReceiveMessageAsync(timeout.Token);
            var response = document.RootElement;
            if (!response.TryGetProperty(SiteBrowserTokens.IdField, out var id))
            {
                QueueEvent(response);
                continue;
            }

            if (id.GetInt32() != commandId)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserProtocolFailure);
            }
            if (response.TryGetProperty(SiteBrowserTokens.ErrorField, out var error))
            {
                var detail = error.TryGetProperty(SiteBrowserTokens.MessageField, out var value)
                    ? value.GetString() : null;
                throw new InvalidOperationException($"{SiteBrowserTokens.BrowserProtocolFailure} {detail}");
            }

            return response.GetProperty(SiteBrowserTokens.ResultField).Clone();
        }
    }

    public async Task<JsonElement> EvaluateAsync(string expression, bool awaitPromise, CancellationToken cancellationToken)
    {
        var response = await CommandAsync(SiteBrowserTokens.RuntimeEvaluate, new Dictionary<string, object?>
        {
            [SiteBrowserTokens.ExpressionField] = expression,
            [SiteBrowserTokens.AwaitPromiseField] = awaitPromise,
            [SiteBrowserTokens.ReturnByValueField] = true,
        }, cancellationToken);
        if (response.TryGetProperty(SiteBrowserTokens.ExceptionDetailsField, out _))
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserProtocolFailure);
        }

        return response.GetProperty(SiteBrowserTokens.ResultField)
            .TryGetProperty(SiteBrowserTokens.ExpressionValueField, out var result)
            ? result.Clone() : default;
    }

    public async Task<bool> WaitForExpressionAsync(string predicate, CancellationToken cancellationToken)
    {
        var script = SiteBrowserUiTokens.WaitScriptPrefix + predicate + SiteBrowserUiTokens.WaitScriptSuffix;
        var result = await EvaluateAsync(script, awaitPromise: true, cancellationToken);
        return result.ValueKind == JsonValueKind.True;
    }

    public IReadOnlyList<JsonElement> DrainEvents()
    {
        var result = new List<JsonElement>();
        while (events.TryDequeue(out var item))
        {
            result.Add(item);
        }
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(SiteBrowserTokens.BrowserCommandTimeoutMilliseconds);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, SiteBrowserTokens.EmptyReason, timeout.Token);
            }
        }
        catch (WebSocketException)
        {
            // Browser process ownership performs the final bounded socket teardown.
        }
        catch (OperationCanceledException)
        {
            // Browser process ownership performs the final bounded socket teardown.
        }
        finally
        {
            socket.Dispose();
            sendGate.Dispose();
        }
    }

    private void QueueEvent(JsonElement message)
    {
        if (events.Count >= SiteBrowserTokens.MaximumQueuedEvents)
        {
            throw new InvalidOperationException(SiteBrowserTokens.BrowserResponseExceeded);
        }

        events.Enqueue(message.Clone());
    }

    private static async Task DisposeAfterConnectionFailure(SiteBrowserCdpClient client)
    {
        try
        {
            await client.DisposeAsync();
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or
            ObjectDisposedException or InvalidOperationException)
        {
            // Preserve the connection failure while releasing the owned socket.
        }
    }

    private async Task<JsonDocument> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        using var payload = new MemoryStream();
        var buffer = new byte[SiteBrowserTokens.WebSocketBufferBytes];
        WebSocketReceiveResult part;
        do
        {
            part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (part.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserProtocolFailure);
            }
            if (payload.Length + part.Count > SiteBrowserTokens.MaximumBrowserMessageBytes)
            {
                throw new InvalidOperationException(SiteBrowserTokens.BrowserResponseExceeded);
            }

            await payload.WriteAsync(buffer.AsMemory(SiteBrowserTokens.Zero, part.Count), cancellationToken);
        } while (!part.EndOfMessage);

        return JsonDocument.Parse(payload.ToArray());
    }

    private sealed record CdpCommand(int Id, string Method,
        [property: JsonPropertyName(SiteBrowserTokens.ParamsField)] object Parameters);
}
