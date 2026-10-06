using System.Net.WebSockets;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal sealed class AdminBrowserCdp : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private int requestId;
    internal AdminBrowserNetwork Network { get; } = new();

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
            if (!response.RootElement.TryGetProperty(AdminBrowserProtocol.IdProperty, out var actual) || actual.GetInt32() != id)
            { Network.Observe(response.RootElement); continue; }
            if (response.RootElement.TryGetProperty(AdminBrowserProtocol.ErrorProperty, out _))
            { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
            return response.RootElement.GetProperty(AdminBrowserProtocol.ResultProperty).Clone();
        }
    }

    internal async Task<JsonElement> EvaluateAsync(string expression, CancellationToken cancellationToken)
    {
        var response = await CommandAsync(AdminBrowserProtocol.RuntimeEvaluate,
            new { expression, returnByValue = true, awaitPromise = true }, cancellationToken);
        if (response.TryGetProperty(AdminBrowserProtocol.ExceptionProperty, out _))
        { throw new InvalidOperationException(AdminBrowserProtocol.BrowserFailure); }
        return response.GetProperty(AdminBrowserProtocol.ResultProperty).GetProperty(AdminBrowserProtocol.ValueProperty).Clone();
    }

    internal async Task WaitAsync(string expression, CancellationToken cancellationToken)
    {
        while (!(await EvaluateAsync(expression, cancellationToken)).GetBoolean())
        { await Task.Delay(TimeSpan.FromMilliseconds(AdminBrowserProtocol.PollMilliseconds), TimeProvider.System, cancellationToken); }
    }

    internal async Task WaitForNetworkIdleAsync(CancellationToken cancellationToken)
    {
        const string Probe = "true";
        do
        {
            await EvaluateAsync(Probe, cancellationToken);
            if (Network.ActiveRequests == 0)
            { return; }
            await Task.Delay(TimeSpan.FromMilliseconds(AdminBrowserProtocol.PollMilliseconds), TimeProvider.System, cancellationToken);
        } while (true);
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
            await output.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        } while (!result.EndOfMessage);
        return output.ToArray();
    }

    public ValueTask DisposeAsync()
    {
        socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
