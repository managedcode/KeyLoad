namespace KeyLoad.Query.Features.Search;

internal static class FilteredSearchErrors
{
    internal const string InvalidAllowlist = "The filtered search allowlist is invalid.";
    internal const string AllowlistExceeded = "The filtered search allowlist exceeds its configured bound.";
    internal const string RequestExceeded = "The search request exceeds its configured byte bound.";
}
