using System.Collections.Immutable;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class CodeQualityCompositionMethods
{
    private const string Add = "AddKeyLoad";
    private const string Build = "BuildKeyLoad";
    private const string Use = "UseKeyLoad";
    private const string Map = "MapKeyLoad";
    private const string Run = "RunKeyLoad";

    public static ImmutableArray<string> All { get; } =
    [
        Add,
        Build,
        Use,
        Map,
        Run
    ];
}
