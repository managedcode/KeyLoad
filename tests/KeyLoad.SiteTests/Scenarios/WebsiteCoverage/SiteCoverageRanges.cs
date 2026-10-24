using System.Text.Json;

namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal static class SiteCoverageRanges
{
    public static IReadOnlyList<SiteCoverageScriptSnapshot> ParseScripts(JsonElement scripts, string runtime,
        string repository, IReadOnlyDictionary<string, SiteCoverageSourceEntry> sources,
        IReadOnlySet<string>? browserOrigins, string receiptIdentity, string receiptSha256)
    {
        if (receiptIdentity.Length == SiteCoverageTokens.Zero || !SiteCoverageSourceManifestWriter.IsHash(receiptSha256))
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        if (scripts.ValueKind != JsonValueKind.Array)
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.EmptyFileFailure);
        }

        if (scripts.GetArrayLength() == SiteCoverageTokens.Zero && runtime != SiteCoverageTokens.BrowserRuntime)
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.EmptyFileFailure);
        }

        var snapshots = new List<SiteCoverageScriptSnapshot>();
        foreach (var script in scripts.EnumerateArray())
        {
            var sourcePath = SiteCoverageSourceResolver.ResolveScriptSource(script, runtime, repository,
                sources, browserOrigins);
            if (sourcePath is null)
            {
                continue;
            }

            var entry = sources[sourcePath];
            var scriptId = SiteCoverageJson.GetString(script, SiteCoverageTokens.ScriptId);
            if (scriptId.Length == SiteCoverageTokens.Zero)
            {
                throw SiteCoverageJson.Invalid(SiteCoverageTokens.JsonFailure);
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
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.JsonFailure);
    }

    private static JsonElement[] ReadFunctions(JsonElement script)
    {
        var functions = script.GetProperty(SiteCoverageTokens.Functions);
        if (functions.ValueKind != JsonValueKind.Array)
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        return functions.EnumerateArray().ToArray();
    }

    private static SiteCoverageFunctionRanges ParseFunction(JsonElement function, string sourcePath, int sourceLength)
    {
        SiteCoverageJson.RequireObject(function, SiteCoverageJson.RuntimeFields.FunctionRequired, SiteCoverageJson.RuntimeFields.NoOptional);
        var name = SiteCoverageJson.GetString(function, SiteCoverageTokens.FunctionName);
        var isBlock = function.GetProperty(SiteCoverageTokens.IsBlockCoverage).ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw SiteCoverageJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure),
        };
        var rangesElement = function.GetProperty(SiteCoverageTokens.Ranges);
        if (rangesElement.ValueKind != JsonValueKind.Array || rangesElement.GetArrayLength() == SiteCoverageTokens.Zero)
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.InvalidCoverageFailure);
        }

        var ranges = rangesElement.EnumerateArray().Select(range => ParseRange(range, sourceLength))
            .OrderBy(range => range.StartOffset).ThenByDescending(range => range.EndOffset).ToArray();
        ValidateRangeTree(ranges);
        var root = ranges[SiteCoverageTokens.Zero];
        return new(sourcePath, name, isBlock, root.StartOffset, root.EndOffset, ranges);
    }

    private static SiteCoverageRange ParseRange(JsonElement range, int sourceLength)
    {
        SiteCoverageJson.RequireObject(range, SiteCoverageJson.RuntimeFields.RangeRequired, SiteCoverageJson.RuntimeFields.NoOptional);
        var start = SiteCoverageJson.GetInt32(range, SiteCoverageTokens.StartOffset);
        var end = SiteCoverageJson.GetInt32(range, SiteCoverageTokens.EndOffset);
        var count = SiteCoverageJson.GetInt64(range, SiteCoverageTokens.Count);
        if (start < SiteCoverageTokens.Zero || end <= start || end > sourceLength || count < SiteCoverageTokens.Zero)
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.OutOfBoundsRangeFailure);
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
                throw SiteCoverageJson.Invalid(SiteCoverageTokens.CrossingRangeFailure);
            }

            if (stack.TryPeek(out parent) && range.StartOffset == parent.StartOffset && range.EndOffset == parent.EndOffset)
            {
                throw SiteCoverageJson.Invalid(SiteCoverageTokens.DuplicateRangeFailure);
            }

            stack.Push(range);
        }

        if (ranges[SiteCoverageTokens.Zero].StartOffset != ranges.Min(range => range.StartOffset) ||
            ranges[SiteCoverageTokens.Zero].EndOffset != ranges.Max(range => range.EndOffset))
        {
            throw SiteCoverageJson.Invalid(SiteCoverageTokens.CrossingRangeFailure);
        }
    }
}
