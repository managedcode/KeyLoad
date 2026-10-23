namespace KeyLoad.Clustering;

/// <summary>Canonical pool keys; Orleans generates each stateless worker activation's actual identity.</summary>
internal static class BackgroundWorkerIdentity
{
    internal const int InterfaceVersion = 2;
    private const string MessagingPool = "messaging";
    private const string AnnPool = "ann";
    private const string TimeSeriesPool = "time-series";
    public static string Messaging() => MessagingPool;
    public static string AnnProjection() => AnnPool;
    public static string TimeSeries() => TimeSeriesPool;
}
