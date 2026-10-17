using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Invokes the genuine RF3 callers and preserves their distinct transport contracts.</summary>
internal static class RequestCqrsFaultCallers
{
    /// <summary>Sends the exact caller-owned command through the real SDK without retrying or changing its ID.</summary>
    /// <param name="client">The real HTTP SDK client.</param>
    /// <param name="command">The exact stable command under observation.</param>
    /// <param name="cancellationToken">The bounded caller token.</param>
    /// <returns>The SDK's actual result, including its typed unknown-outcome classification.</returns>
    internal static Task<Result<CommitReceipt>> CommitSdkAsync(KeyLoadClient client, CommandRequest command,
        CancellationToken cancellationToken)
        => client.CommitAsync(command, cancellationToken);

    /// <summary>Calls the official MCP SDK and retains either its actual tool result or transport exception.</summary>
    /// <param name="client">The real connected official MCP client.</param>
    /// <param name="command">The exact stable command under observation.</param>
    /// <param name="cancellationToken">The bounded caller token.</param>
    /// <returns>The unmodified official result, or the actual transport/cancellation exception.</returns>
    internal static async Task<RequestCqrsFaultMcpObservation> CommitMcpAsync(McpOfficialClient client,
        CommandRequest command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.CallAsync(McpCallerTools.DocumentsCommit, command, cancellationToken)
                .ConfigureAwait(false);
            return new(result, null);
        }
        catch (OperationCanceledException error)
        {
            return new(null, error);
        }
        catch (HttpRequestException error)
        {
            return new(null, error);
        }
        catch (IOException error)
        {
            return new(null, error);
        }
    }
}
