namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedHttpTests
{
    /// <summary>AC-ISO-008: real fetch loads hash-bound compact bytes from the native confined static listener.</summary>
    [Test]
    public async Task AC_ISO_008_NativeHttpAcceptsAuthenticProjection()
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        await using var host = SiteStaticFileHost.Start(fixture.Root);
        var response = await Load(fixture, host.BaseUrl, false, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsTrue();
        await Assert.That(response.GetProperty(SiteIsolatedFields.Result).GetInt32()).IsEqualTo(SiteIsolatedInventory.ControlWorkers(fixture.Inputs.Site.MeasuredRevision));
    }

    /// <summary>AC-ISO-008: controlled malformed files are parser data; native HTTP/fetch/crypto remain genuine.</summary>
    [Test]
    [Arguments("hash")]
    [Arguments("duplicate")]
    [Arguments("utf8")]
    [Arguments("catalogSize")]
    [Arguments("projectionSize")]
    [Arguments("missing")]
    [Arguments("abort")]
    public async Task AC_ISO_008_NativeHttpRejectsCorruptionBoundsMissingAndCancellation(string corruption)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temporary.Path, "isolated"));
        var catalog = Path.Combine(temporary.Path, "catalog.json");
        var projection = Path.Combine(temporary.Path, "isolated", "projection.json");
        File.Copy(fixture.Catalog, catalog);
        File.Copy(fixture.Projection, projection);
        await CorruptAsync(catalog, projection, corruption, token);
        await using var host = SiteStaticFileHost.Start(temporary.Path);
        var response = await Load(fixture, host.BaseUrl, corruption == "abort", token);
        await Assert.That(response.GetProperty(SiteIsolatedFields.Ok).GetBoolean()).IsFalse();
        if (corruption == "abort")
        {
            await Assert.That(response.GetProperty(SiteIsolatedFields.Error).GetString()).IsEqualTo("AbortError");
        }
    }

    private static Task<System.Text.Json.JsonElement> Load(SiteIsolatedFixture fixture, string baseUrl,
        bool abort, CancellationToken token) => SiteIsolatedNodeProcess.RunAsync(fixture.Inputs.Site, new
        {
            operation = "load",
            repository = fixture.Inputs.Site.Repository,
            catalogUrl = baseUrl + "catalog.json",
            baseUrl,
            abort,
        }, token);

    private static async Task CorruptAsync(string catalog, string projection, string corruption, CancellationToken token)
    {
        switch (corruption)
        {
            case "hash":
                await File.AppendAllTextAsync(projection, " ", token);
                break;
            case "duplicate":
            {
                var original = await File.ReadAllTextAsync(catalog, token);
                await File.WriteAllTextAsync(catalog, "{\"schemaVersion\":999," + original[1..], token);
                break;
            }
            case "utf8":
                await File.WriteAllBytesAsync(catalog, [0xff], token);
                break;
            case "catalogSize":
                await File.WriteAllBytesAsync(catalog, new byte[65_537], token);
                break;
            case "projectionSize":
                await File.WriteAllBytesAsync(projection, new byte[4_194_305], token);
                break;
            case "missing":
                File.Delete(projection);
                break;
            case "abort":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
    }
}
