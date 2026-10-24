namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class BenchmarkAdditionalImages
{
    internal const string TimescaleImage = "timescale/timescaledb", TimescaleTag = "2.30.2-pg18";
    internal const string TimescaleDigest = "sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    internal const string SqlServerImage = "mcr.microsoft.com/mssql/server", SqlServerTag = "2025-latest";
    internal const string SqlServerDigest = "sha256:245f6b1c8903edae52cb20fa31419cefcde1a49938bb8c3447099011fc871ca3";
    internal const string MySqlImage = "mysql", MySqlTag = "8.4";
    internal const string MySqlDigest = "sha256:76f0358884cb92d9a086d24e5671c38048d3fef40f2588703d73eb55392d6b69";
    internal const string ClickHouseImage = "clickhouse/clickhouse-server", ClickHouseTag = "26.9";
    internal const string ClickHouseDigest = "sha256:19bfacb135479a892551a8155cfbbc8211aedf2aa888cd91950b9ee7e76cc7e3";
    internal const string InfluxImage = "influxdb", InfluxTag = "3.12-core";
    internal const string InfluxDigest = "sha256:1c8200f75657c7586678b52570f8c78aedfa2cd42e59c238a733423aab6bd406";
    internal const int DigestPrefixLength = 7;
}
