using System.Globalization;

namespace KeyLoad.UnitTests;

internal sealed record ReplicaReplayLogEntry(int Event, Dictionary<string, object?> Fields, string Rendered)
{
    internal long Number(string name) => Convert.ToInt64(Fields[name], CultureInfo.InvariantCulture);
}
