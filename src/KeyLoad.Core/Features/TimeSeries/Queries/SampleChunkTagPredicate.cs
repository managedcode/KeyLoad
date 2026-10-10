using System.Text.Json;

namespace KeyLoad.Core.Features.TimeSeries;

internal static class SampleChunkTagPredicate
{
    internal static string[]? Prepare(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, ReadSampleChunkWindowRequest request)
    {
        if (request.TagPointer is null && request.TagValue is null)
        { return null; }
        if (request.TagPointer is null || request.TagValue is null)
        { throw Errors.Fail(ErrorCode.Validation, SampleChunkLifecycleProtocol.Invalid); }
        database.Authorization.RequireFieldUse(principal, resource, request.TagPointer);
        return JsonData.PathSegments(request.TagPointer);
    }

    internal static bool Matches(string json, string[]? path, string? expected)
    {
        if (path is null)
        { return true; }
        using var document = JsonDocument.Parse(json);
        return JsonData.Scalar(document.RootElement, path) is string text
            && string.Equals(text, expected, StringComparison.Ordinal);
    }
}
