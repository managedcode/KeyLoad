namespace KeyLoad;

/// <summary>Canonical bounded rollup protocol identities and safe failure details.</summary>
public static class SampleRollupProtocol
{
    /// <summary>Identifies the bounded public HTTP rollup read.</summary>
    public const string ReadRoute = "/v1/series/rollups/read";
    /// <summary>Identifies the canonical MCP rollup read operation.</summary>
    public const string ReadTool = "keyload_series_read_rollup";
    /// <summary>Identifies explicit derived-bucket refresh mutations.</summary>
    public const string RefreshKind = "refreshSampleRollup";
    /// <summary>Identifies revision-preserving derived-bucket drops.</summary>
    public const string DropKind = "dropSampleRollup";
    /// <summary>Provides the safe invalid range or revision detail.</summary>
    public const string InvalidRange = "The time-series rollup range or revision is invalid.";
    /// <summary>Provides the safe optimistic revision conflict detail.</summary>
    public const string WrongRevision = "The time-series rollup revision changed.";
    /// <summary>Provides the safe changed raw-history or retention watermark detail.</summary>
    public const string Stale = "The time-series rollup is stale or its range precedes the retention floor.";
    /// <summary>Provides the safe cumulative read-byte budget rejection detail.</summary>
    public const string ByteBudget = "The time-series rollup read byte budget is exceeded.";
    /// <summary>Provides the safe per-series bucket-identity budget rejection detail.</summary>
    public const string BucketBudget = "The time-series rollup bucket identity limit is exceeded.";
    /// <summary>Provides the safe absent bucket detail.</summary>
    public const string Missing = "The time-series rollup bucket is absent.";
    /// <summary>Provides the safe invalid canonical rollup state detail.</summary>
    public const string Corrupt = "The time-series rollup state or sample is corrupt.";
    /// <summary>Provides the safe unsupported current format detail.</summary>
    public const string Unsupported = "The time-series rollup format is unsupported.";
}
