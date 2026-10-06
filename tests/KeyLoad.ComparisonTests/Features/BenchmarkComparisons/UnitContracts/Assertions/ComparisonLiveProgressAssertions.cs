using System.Globalization;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ComparisonLiveProgressAssertions
{
    private static readonly string[] Phases = ["oracle", "initialize", "warmup", "prepare", "measure", "validate", "complete"];

    internal static async Task AssertClosedAsync(string line)
    {
        await Assert.That(line.Length <= 512).IsTrue();
        var fields = line.Split(' ');
        await Assert.That(fields.Length).IsEqualTo(7);
        await Assert.That(fields[0]).IsEqualTo("KeyLoadBenchmarkProgress");
        await Assert.That(Phases.Contains(Value(fields[1], "phase"), StringComparer.Ordinal)).IsTrue();
        var repetition = int.Parse(Value(fields[2], "repetition"), CultureInfo.InvariantCulture);
        var completed = int.Parse(Value(fields[3], "completed"), CultureInfo.InvariantCulture);
        var total = int.Parse(Value(fields[4], "total"), CultureInfo.InvariantCulture);
        var failed = int.Parse(Value(fields[5], "failed"), CultureInfo.InvariantCulture);
        var elapsed = double.Parse(Value(fields[6], "elapsedSeconds"), CultureInfo.InvariantCulture);
        await Assert.That(repetition >= 0 && failed >= 0 && failed <= completed && completed <= total).IsTrue();
        await Assert.That(double.IsFinite(elapsed) && elapsed >= 0).IsTrue();
        await Assert.That(fields[6].Contains(',', StringComparison.Ordinal)).IsFalse();
    }

    private static string Value(string field, string name)
    {
        var parts = field.Split('=');
        if (parts.Length != 2 || parts[0] != name)
        {
            throw new InvalidDataException();
        }
        return parts[1];
    }
}
