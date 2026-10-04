using System.Text;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>AC-TEST-007: native Docker and redirected stream diagnostics stay bounded and private.</summary>
internal sealed class ContainerRestartNativeDiagnosticsTests
{
    private const string PrivateCanary = "docker-private-canary";
    private const string UnavailableLine = "Docker restart inspection unavailable.";

    [Test]
    public async Task AcTest007ValidNativeDockerRecordProjectsOnlyClosedFields()
    {
        var id = new string('A', 64);
        var record = string.Join('|', id, "running", "17", "true",
            "2026-07-28T10:11:12.000000000Z", "2026-07-28T10:11:13.000000000Z", PrivateCanary);

        var line = ContainerRestartDockerInspection.FormatInspection(record);

        await Assert.That(line.Contains("id=" + id, StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("state=running", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("exit=17", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("oomKilled=True", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("hasError=True", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains(PrivateCanary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcTest007MalformedNativeDockerFieldsReturnFixedPrivateMarker()
    {
        var valid = string.Join('|', new string('b', 64), "exited", "0", "false",
            "2026-07-28T10:11:12Z", "2026-07-28T10:11:13+00:00", "");
        var invalidRecords = new[]
        {
            "too|few|" + PrivateCanary,
            valid.Replace(new string('b', 64), PrivateCanary, StringComparison.Ordinal),
            valid.Replace(new string('b', 64), new string('g', 64), StringComparison.Ordinal),
            valid.Replace("exited", PrivateCanary, StringComparison.Ordinal),
            valid.Replace("|0|", "|-1|", StringComparison.Ordinal),
            valid.Replace("|0|", "|" + PrivateCanary + "|", StringComparison.Ordinal),
            valid.Replace("|false|", "|" + PrivateCanary + "|", StringComparison.Ordinal),
            valid.Replace("2026-07-28T10:11:12Z", PrivateCanary, StringComparison.Ordinal),
            valid.Replace("2026-07-28T10:11:13+00:00", "2026-07-28T12:11:13+02:00", StringComparison.Ordinal)
        };

        foreach (var record in invalidRecords)
        {
            var line = ContainerRestartDockerInspection.FormatInspection(record);
            await Assert.That(line).IsEqualTo(UnavailableLine);
            await Assert.That(line.Contains(PrivateCanary, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task AcTest007BoundedActualStreamReaderDrainsAfterRetainingPrefix()
    {
        const int retainedCharacters = 5;
        const string output = "prefix-and-the-rest-of-the-native-output";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(output));
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var prefix = await ContainerRestartProcessIo.ReadBoundedAsync(reader, retainedCharacters,
            CancellationToken.None);
        var unread = await reader.ReadToEndAsync();

        await Assert.That(prefix).IsEqualTo("prefi");
        await Assert.That(unread).IsEmpty();
        await Assert.That(stream.Position).IsEqualTo(stream.Length);
    }
}
