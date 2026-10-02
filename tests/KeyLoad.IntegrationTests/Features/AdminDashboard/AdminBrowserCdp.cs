using System.Net.WebSockets;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed class AdminBrowserCdp : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private int requestId;

    internal async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
        => await socket.ConnectAsync(endpoint, cancellationToken);

    internal async Task<JsonElement> CommandAsync(string method, object arguments, CancellationToken cancellationToken)
    {
        var id = ++requestId;
        var request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = arguments });
        await socket.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
        while (true)
        {
            using var response = JsonDocument.Parse(await ReceiveAsync(cancellationToken));
            if (!response.RootElement.TryGetProperty("id", out var actual) || actual.GetInt32() != id)
            { continue; }
            if (response.RootElement.TryGetProperty("error", out _))
            { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
            return response.RootElement.GetProperty("result").Clone();
        }
    }

    internal async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var response = await CommandAsync(AdminBrowserProtocol.RuntimeEvaluate,
            new { expression, returnByValue = true, awaitPromise = true }, cancellationToken);
        if (response.TryGetProperty("exceptionDetails", out _))
        { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
        return response.GetProperty("result").GetProperty("value").Clone();
    }

    internal async Task WaitAsync(string expression, CancellationToken cancellationToken)
    {
        while (!(await EvaluateAsync(expression, cancellationToken)).GetBoolean())
        { await Task.Delay(AdminBrowserProtocol.PollMilliseconds, cancellationToken); }
    }

    private async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[AdminBrowserProtocol.ReceiveBufferBytes];
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close || output.Length + result.Count > AdminBrowserProtocol.MaximumReplyBytes)
            { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
            output.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return output.ToArray();
    }

    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
