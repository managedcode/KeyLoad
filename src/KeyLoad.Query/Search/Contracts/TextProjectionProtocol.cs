namespace KeyLoad.Query.Features.Search;

/// <summary>Permanent generated contract and algorithm identities for native text generations.</summary>
public static class TextProjectionProtocol
{
    /// <summary>Stable native scope alias.</summary>
    public const string ScopeAlias = "keyload.search.text-projection.scope.v1";
    /// <summary>Canonical text normalization and tokenization version.</summary>
    public const string TokenizerVersion = "keyload-nfkc-rune-lower-v1";
    /// <summary>Stable native posting hash byte order and algorithm.</summary>
    public const string HashVersion = "sha256-le64-v1";
}
