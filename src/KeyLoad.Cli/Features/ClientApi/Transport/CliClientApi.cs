using System.Globalization;
using System.Resources;
using System.Text.Json;
using KeyLoad.Client;
using ManagedCode.Communication;

namespace KeyLoad.Cli.Features.ClientApi;

internal static class CliClientApi
{
    private const string AdminKeyPropertyName = "AdminKey";
    private const string KeyEnvironmentVariableName = "KEYLOAD_API_KEY";
    private const int StatusTimeoutSeconds = 30;
    private const string MessagesBaseName = "KeyLoad.Cli.Features.ClientApi.CliClientMessages";
    private const string HelpMessageKey = "Help";
    private const string MissingCredentialMessageKey = "MissingCredential";
    private const string NodeUnavailableMessageKey = "NodeUnavailable";
    private static readonly ResourceManager Messages = new(
        MessagesBaseName,
        typeof(CliClientApi).Assembly);

    public static async Task StatusAsync(string[] args)
    {
        var key = GetCredential(args);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(args[1]),
            Timeout = TimeSpan.FromSeconds(StatusTimeoutSeconds)
        };
        var result = await new KeyLoadClient(http, key).StatusAsync();
        if (result.IsFailed)
        {
            ThrowNodeFailure(result.Problem);
        }

        Console.WriteLine(JsonSerializer.Serialize(result.Value, JsonDefaults.Options));
    }

    public static void Help() => Console.WriteLine(GetMessage(HelpMessageKey));

    private static string GetCredential(string[] args) =>
        Environment.GetEnvironmentVariable(KeyEnvironmentVariableName)
        ?? (args.Length == 3 ? ProfileKey(args[2]) : null)
        ?? throw Errors.Fail(ErrorCode.Unauthenticated, GetMessage(MissingCredentialMessageKey));

    private static string ProfileKey(string path)
    {
        using var profile = JsonDocument.Parse(File.ReadAllBytes(path));
        return profile.RootElement.GetProperty(AdminKeyPropertyName).GetString()!;
    }

    private static void ThrowNodeFailure(Problem problem)
    {
        var code = Enum.TryParse<ErrorCode>(problem.ErrorCode, out var parsedCode)
            ? parsedCode
            : ErrorCode.OwnershipLost;
        throw Errors.Fail(code, problem.Detail ?? GetMessage(NodeUnavailableMessageKey));
    }

    private static string GetMessage(string key) => Messages.GetString(key, CultureInfo.InvariantCulture)!;
}
