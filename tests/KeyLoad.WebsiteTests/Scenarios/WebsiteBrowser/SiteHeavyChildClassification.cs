using System.Text.Json;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal static class SiteHeavyChildClassification
{
    internal static bool IsProjection(JsonElement request) =>
        (ReadOperation(request)).Value == SiteHeavyChildTokens.Produce;

    internal static bool IsArchive(JsonElement request)
    {
        var operation = (ReadOperation(request)).Value;
        if (operation == SiteIsolatedGitHubFields.InputsOperation)
        {
            return true;
        }

        return operation == SiteIsolatedGitHubFields.CliOperation &&
            request.TryGetProperty(SiteHeavyChildTokens.Arguments, out var arguments) &&
            arguments.ValueKind == JsonValueKind.Array && arguments.GetArrayLength() > SiteTokens.Zero &&
            arguments[SiteTokens.Zero].ValueKind == JsonValueKind.String &&
            arguments[SiteTokens.Zero].GetString() == SiteIsolatedGitHubFields.VerifyInputs;
    }

    private static global::ManagedCode.Communication.Result<string?> ReadOperation(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && request.TryGetProperty(SiteHeavyChildTokens.Operation, out var operation) && operation.ValueKind == JsonValueKind.String
            ? ((string?)(operation.GetString())) is { } outcomeValue1 ? global::ManagedCode.Communication.Result<string?>.Succeed(outcomeValue1) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadOperation)) : global::ManagedCode.Communication.Result<string?>.FailNotFound(nameof(ReadOperation));
}
