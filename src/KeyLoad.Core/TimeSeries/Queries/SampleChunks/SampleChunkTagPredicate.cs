using System.Text.Json;

namespace KeyLoad.Core.TimeSeries;

internal static class SampleChunkTagPredicate
{
    internal static (bool HasValue, string[]? Value) Prepare(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, ReadSampleChunkWindowRequest request)
    {
        if (request.TagPointer is null && request.TagValue is null)
        {
            return (false, default(string[]?));
        }
        if (request.TagPointer is null || request.TagValue is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SampleChunkLifecycleProtocol.Invalid);
        }
        database.Authorization.RequireFieldUse(principal, resource, request.TagPointer);
        return (true, ((string[]?)(JsonData.PathSegments(request.TagPointer))));
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
