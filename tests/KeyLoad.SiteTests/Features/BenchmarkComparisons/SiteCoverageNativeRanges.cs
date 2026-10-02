using System.Text.Json;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteCoverageRange(int StartOffset, int EndOffset, long Count);

internal sealed record SiteCoverageFunctionRanges(string SourcePath, string FunctionName, bool IsBlockCoverage,
    int FunctionStart, int FunctionEnd, IReadOnlyList<SiteCoverageRange> Ranges);

internal sealed record SiteCoverageScriptSnapshot(string ReceiptIdentity, string ReceiptSha256, string ScriptId,
    int? ExecutionContextId, string SourcePath, IReadOnlyList<SiteCoverageFunctionRanges> Functions);

internal static class SiteCoverageNativeRanges
{
    public static IReadOnlyList<SiteCoverageScriptSnapshot> ParseScripts(JsonElement scripts, string runtime,
        string repository, IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources,
        IReadOnlySet<string>? browserOrigins, string receiptIdentity, string receiptSha256)
    {
        if (receiptIdentity.Length == SiteCoverageTokens.Zero || !SiteCoverageSourceManifestWriter.IsHash(receiptSha256))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        if (scripts.ValueKind != JsonValueKind.Array || scripts.GetArrayLength() == SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.EmptyNativeFileFailure);
        }

        var snapshots = new List<SiteCoverageScriptSnapshot>();
        foreach (var script in scripts.EnumerateArray())
        {
            var sourcePath = SiteCoverageNativeSourceResolver.ResolveScriptSource(script, runtime, repository,
                sources, browserOrigins);
            if (sourcePath is null)
            {
                continue;
            }

            var entry = sources[sourcePath];
            var scriptId = SiteCoverageNativeJson.GetString(script, SiteCoverageTokens.ScriptId);
            if (scriptId.Length == SiteCoverageTokens.Zero)
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.JsonFailure);
            }

            var contextId = ReadExecutionContext(script);
            var functions = ReadFunctions(script).Select(function => ParseFunction(function, sourcePath,
                entry.Utf16Length)).ToArray();
            snapshots.Add(new(receiptIdentity, receiptSha256, scriptId, contextId, sourcePath, functions));
        }

        return snapshots;
    }

    private static int? ReadExecutionContext(JsonElement script)
    {
        if (!script.TryGetProperty(SiteCoverageTokens.ExecutionContextId, out var context))
        {
            return null;
        }

        return context.TryGetInt32(out var value) ? value :
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.JsonFailure);
    }

    private static JsonElement[] ReadFunctions(JsonElement script)
    {
        var functions = script.GetProperty(SiteCoverageTokens.Functions);
        if (functions.ValueKind != JsonValueKind.Array)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        return functions.EnumerateArray().ToArray();
    }

    private static SiteCoverageFunctionRanges ParseFunction(JsonElement function, string sourcePath, int sourceLength)
    {
        SiteCoverageNativeJson.RequireObject(function, SiteCoverageNativeJson.NativeFields.FunctionRequired, SiteCoverageNativeJson.NativeFields.NoOptional);
        var name = SiteCoverageNativeJson.GetString(function, SiteCoverageTokens.FunctionName);
        var isBlock = function.GetProperty(SiteCoverageTokens.IsBlockCoverage).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure),
        };
        var rangesElement = function.GetProperty(SiteCoverageTokens.Ranges);
        if (rangesElement.ValueKind != JsonValueKind.Array || rangesElement.GetArrayLength() == SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        var ranges = rangesElement.EnumerateArray().Select(range => ParseRange(range, sourceLength))
            .OrderBy(range => range.StartOffset).ThenByDescending(range => range.EndOffset).ToArray();
        ValidateRangeTree(ranges);
        var root = ranges[SiteCoverageTokens.Zero];
        return new(sourcePath, name, isBlock, root.StartOffset, root.EndOffset, ranges);
    }

    private static SiteCoverageRange ParseRange(JsonElement range, int sourceLength)
    {
        SiteCoverageNativeJson.RequireObject(range, SiteCoverageNativeJson.NativeFields.RangeRequired, SiteCoverageNativeJson.NativeFields.NoOptional);
        var start = SiteCoverageNativeJson.GetInt32(range, SiteCoverageTokens.StartOffset);
        var end = SiteCoverageNativeJson.GetInt32(range, SiteCoverageTokens.EndOffset);
        var count = SiteCoverageNativeJson.GetInt64(range, SiteCoverageTokens.Count);
        if (start < SiteCoverageTokens.Zero || end <= start || end > sourceLength || count < SiteCoverageTokens.Zero)
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.OutOfBoundsRangeFailure);
        }

        return new(start, end, count);
    }

    private static void ValidateRangeTree(SiteCoverageRange[] ranges)
    {
        var stack = new Stack<SiteCoverageRange>();
        foreach (var range in ranges)
        {
            SiteCoverageRange? parent;
            while (stack.TryPeek(out parent) && range.StartOffset >= parent.EndOffset)
            {
                stack.Pop();
            }

            if (stack.TryPeek(out parent) && range.EndOffset > parent.EndOffset)
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.CrossingRangeFailure);
            }

            if (stack.TryPeek(out parent) && range.StartOffset == parent.StartOffset && range.EndOffset == parent.EndOffset)
            {
                throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.DuplicateRangeFailure);
            }

            stack.Push(range);
        }

        if (ranges[SiteCoverageTokens.Zero].StartOffset != ranges.Min(range => range.StartOffset) ||
            ranges[SiteCoverageTokens.Zero].EndOffset != ranges.Max(range => range.EndOffset))
        {
            throw SiteCoverageNativeJson.Invalid(SiteCoverageTokens.CrossingRangeFailure);
        }
    }
}
