using System.Globalization;
using System.Resources;
using System.Text.Json;
using KeyLoad.Client;
using ManagedCode.Communication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.ClientApi;

internal static class CliClientApi
{
    private const string MessagesBaseName = "KeyLoad.Cli.Features.ClientApi.CliClientMessages";
    private const string HelpMessageKey = "Help";
    internal const string MissingCredentialMessageKey = "MissingCredential";
    private const string NodeUnavailableMessageKey = "NodeUnavailable";
    private static readonly ResourceManager Messages = new(
        MessagesBaseName,
        typeof(CliClientApi).Assembly);

    public static async Task StatusAsync(IOptions<CliConnectionOptions> connectionOptions,
        IOptions<CliExecutionOptions> executionOptions, IOptions<KeyLoadClientExecutionOptions> clientExecutionOptions)
    {
        var connection = connectionOptions.Value;
        var timeout = executionOptions.Value.StatusTimeout;
        using var http = new HttpClient
        {
            BaseAddress = connection.Endpoint,
            Timeout = timeout
        };
        var result = await new KeyLoadClient(http, connection.ApiKey, clientExecutionOptions).StatusAsync();
        if (result.IsFailed)
        {
            ThrowNodeFailure(result.Problem);
        }

        Console.WriteLine(JsonSerializer.Serialize(result.Value, JsonDefaults.Options));
    }

    public static void Help() => Console.WriteLine(GetMessage(HelpMessageKey));

    private static void ThrowNodeFailure(Problem problem)
    {
        var code = Enum.TryParse<ErrorCode>(problem.ErrorCode, out var parsedCode)
            ? parsedCode
            : ErrorCode.OwnershipLost;
        throw Errors.Fail(code, problem.Detail ?? GetMessage(NodeUnavailableMessageKey));
    }

    internal static string GetMessage(string key) => Messages.GetString(key, CultureInfo.InvariantCulture)!;
}
