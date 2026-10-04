using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Stable tokens and validation diagnostics for Q1.Search.v1.</summary>
internal static class SqlGraphSearchSyntax
{
    internal const string Search = "SEARCH";
    internal const string Text = "TEXT";
    internal const string Match = "MATCH";
    internal const string Weight = "WEIGHT";
    internal const string Vector = "VECTOR";
    internal const string Space = "SPACE";
    internal const string Scope = "SCOPE";
    internal const string Graph = "GRAPH";
    internal const string Seeds = "SEEDS";
    internal const string Depth = "DEPTH";
    internal const string Vertices = "VERTICES";
    internal const string Edges = "EDGES";
    internal const string Labels = "LABELS";
    internal const string Retrieve = "RETRIEVE";
    internal const string Expand = "EXPAND";
    internal const string Allow = "ALLOW";
    internal const string Ids = "IDS";
    internal const string Limit = "LIMIT";
    internal const string Fusion = "FUSION";
    internal const string VectorSpaceDetail = "A valid vector space and matching finite vector parameter are required.";
    internal const string ParameterDetail = "A named SQL graph-search parameter is missing or has the wrong type.";
    internal const string InvalidDetail = "The SQL graph-search statement is invalid for Q1.Search.v1.";
    internal const string WrapperDetail = "The SQL graph-search request is invalid or exceeds its configured bound.";
    internal const string ParameterBudgetDetail = "The SQL graph-search parameter budget is exceeded.";
    internal const string ProfileName = "graph-search-v1";
    internal const int Version = 1;
    internal const int MaximumParameters = 256;
    internal const int DefaultLimit = 10;
    internal const int DefaultFusion = 60;
    internal const double DefaultWeight = 1;
    internal const int MaximumVectorDimension = 4_096;

    internal static KeyLoadException Invalid() => Errors.Fail(ErrorCode.Validation, InvalidDetail);
}
