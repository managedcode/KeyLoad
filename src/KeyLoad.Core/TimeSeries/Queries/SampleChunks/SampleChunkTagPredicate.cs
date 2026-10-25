using System.Text.Json;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleChunkTagPredicate
{
    internal static global::ManagedCode.Communication.Result<string[]?> Prepare(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, ReadSampleChunkWindowRequest request)
    {
        if (request.TagPointer is null && request.TagValue is null)
        {
            return global::ManagedCode.Communication.Result<string[]?>.FailNotFound(nameof(Prepare));
        }
        if (request.TagPointer is null || request.TagValue is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkLifecycleProtocol.Invalid);
        }
        database.Authorization.RequireFieldUse(principal, resource, request.TagPointer);
        return global::ManagedCode.Communication.Result<string[]?>.Succeed(((string[]?)(JsonData.PathSegments(request.TagPointer))));
    }

    internal static bool Matches(string json, string[]? path, string? expected)
    {
        if (path is null)
        {
            return true;
        }
        using var document = JsonDocument.Parse(json);
        return (JsonData.Scalar(document.RootElement, path)).Value.Value is string text
            && string.Equals(text, expected, StringComparison.Ordinal);
    }
}
