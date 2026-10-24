namespace KeyLoad.Comparisons;

/// <summary>Canonical identities for the SQL and analytical benchmark additions.</summary>
public static class BenchmarkAdditionalTargets
{
    /// <summary>The TimescaleDB hypertable target.</summary>
    public const string Timescale = "TimescaleDB";
    /// <summary>The Microsoft SQL Server target.</summary>
    public const string SqlServer = "SQL Server";
    /// <summary>The MySQL InnoDB target.</summary>
    public const string MySql = "MySQL";
    /// <summary>The ClickHouse MergeTree target.</summary>
    public const string ClickHouse = "ClickHouse";
    /// <summary>The InfluxDB 3 Core target.</summary>
    public const string InfluxDb = "InfluxDB";
}
