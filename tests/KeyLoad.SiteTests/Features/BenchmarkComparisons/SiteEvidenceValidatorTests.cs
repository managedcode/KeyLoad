using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Exercises production evidence validators and raw-byte loading through real processes and HTTP.</summary>
internal sealed class SiteEvidenceValidatorTests
{
    /// <summary>Accepts the authentic historical reports and a catalog emitted by the production builder.</summary>
    [Test]
    public async Task AC_BC_015_ProductionValidatorsAcceptAuthenticReportsAndBuilderCatalog()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(build.StandardError.Length).IsEqualTo(SiteTokens.Zero);

        var catalogPath = Path.Combine(temporary.Output, SiteTokens.DataDirectory, SiteTokens.CatalogFile);
        var catalogBytes = await File.ReadAllBytesAsync(catalogPath, token);
        using var catalog = JsonDocument.Parse(catalogBytes);
        var acceptedCatalog = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.CatalogOperation,
            Value: catalog.RootElement.Clone()), token);
        await Assert.That(acceptedCatalog.Accepted).IsTrue();
        await Assert.That(acceptedCatalog.Value.GetProperty(SiteTokens.Runs).GetArrayLength()).IsEqualTo(SiteTokens.ProfileNames.Length);

        foreach (var profile in SiteTokens.ProfileNames)
        {
            var reportPath = Path.Combine(inputs.Reports, profile, SiteTokens.ReportFile);
            using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(reportPath, token));
            await Assert.That(report.RootElement.GetProperty(SiteTokens.SchemaVersion).GetInt32()).IsEqualTo(SiteTokens.ReportSchemaNumber);
            var accepted = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.ReportOperation,
                Value: report.RootElement.Clone(), ExpectedRevision: inputs.MeasuredRevision), token);
            await Assert.That(accepted.Accepted).IsTrue();

            var hash = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.HashOperation, FilePath: reportPath), token);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(reportPath, token)));
            await Assert.That(hash.Value.GetString()).IsEqualTo(expectedHash);
        }
    }

    /// <summary>Rejects malformed paths, hashes, revisions, missing fields, and duplicate catalog entries.</summary>
    [Test]
    public async Task AC_BC_015_CatalogValidatorRejectsPathHashRevisionAndCompletenessViolations()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        var catalogPath = Path.Combine(temporary.Output, SiteTokens.DataDirectory, SiteTokens.CatalogFile);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(catalogPath, token))!.AsObject();
        var invalidCatalogs = new List<JsonObject>
        {
            ChangeEntry(original, SiteTokens.Report, SiteTokens.InvalidReportPath),
            ChangeEntry(original, SiteTokens.Sha256, SiteTokens.MalformedHash),
            ChangeEntry(original, SiteTokens.EvidenceUrl, SiteTokens.ForeignEvidenceUrl),
            ChangeEntry(original, SiteTokens.SourceRevision, new(SiteTokens.HexZero, SiteTokens.ShaLength)),
            RemoveEntryField(original, SiteTokens.Report),
            DuplicateFirstRun(original),
        };

        foreach (var invalid in invalidCatalogs)
        {
            var response = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.CatalogOperation,
                Value: JsonSerializer.SerializeToElement(invalid, SiteTokens.JsonOptions)), token);
            await Assert.That(response.Accepted).IsFalse();
            await Assert.That(response.Property(SiteTokens.Error).GetString()?.Length > SiteTokens.Zero).IsTrue();
        }
    }

    /// <summary>Verifies raw report bytes over HTTP and rejects a copied file changed after catalog hashing.</summary>
    [Test]
    public async Task AC_BC_015_LoadReportVerifiesAuthenticBytesAcrossRealHttpAndRejectsTampering()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var build = await SiteBuilderProcess.RunAsync(inputs, inputs.Reports, temporary.Output, token);
        await Assert.That(build.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        using var catalog = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(temporary.Output, SiteTokens.DataDirectory, SiteTokens.CatalogFile), token));
        var entry = catalog.RootElement.GetProperty(SiteTokens.Runs)[SiteTokens.Zero].Clone();
        await using var host = SiteStaticFileHost.Start(temporary.Output);
        var baseUrl = host.BaseUrl + SiteTokens.DataDirectoryUrl;
        var loaded = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.LoadReportOperation,
            Entry: entry, BaseUrl: baseUrl), token);
        await Assert.That(loaded.Accepted).IsTrue();
        await Assert.That(loaded.Value.GetProperty(SiteTokens.SourceRevision).GetString()).IsEqualTo(inputs.MeasuredRevision);

        var outsideEntry = JsonNode.Parse(entry.GetRawText())!.AsObject();
        outsideEntry[SiteTokens.Report] = SiteTokens.InvalidReportPath;
        var outside = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.LoadReportOperation,
            Entry: JsonSerializer.SerializeToElement(outsideEntry, SiteTokens.JsonOptions), BaseUrl: baseUrl), token);
        await Assert.That(outside.Accepted).IsFalse();
        var wrongBase = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.LoadReportOperation,
            Entry: entry, BaseUrl: host.BaseUrl + SiteTokens.MissingDataDirectoryUrl), token);
        await Assert.That(wrongBase.Accepted).IsFalse();
        var credentialBase = new UriBuilder(host.BaseUrl) { UserName = SiteTokens.StaticHostCredentialUser }.Uri.AbsoluteUri + SiteTokens.DataDirectoryUrl;
        var credentials = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.LoadReportOperation,
            Entry: entry, BaseUrl: credentialBase), token);
        await Assert.That(credentials.Accepted).IsFalse();

        var reportPath = Path.Combine(temporary.Output, SiteTokens.DataDirectory,
            entry.GetProperty(SiteTokens.Report).GetString()!.Replace(SiteTokens.UrlPathSeparatorCharacter, Path.DirectorySeparatorChar));
        var originalBytes = await File.ReadAllBytesAsync(reportPath, token);
        var changedBytes = new byte[originalBytes.Length + SiteTokens.One];
        changedBytes[SiteTokens.Zero] = SiteTokens.JsonLeadingWhitespace;
        originalBytes.CopyTo(changedBytes, SiteTokens.One);
        await File.WriteAllBytesAsync(reportPath, changedBytes, token);
        var rejected = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.LoadReportOperation,
            Entry: entry, BaseUrl: baseUrl), token);
        await Assert.That(rejected.Accepted).IsFalse();
        await Assert.That(rejected.Property(SiteTokens.Error).GetString()?.Length > SiteTokens.Zero).IsTrue();
    }

    /// <summary>Rejects revision, tuple, numeric, sample, and JSON corruption in actual validator calls.</summary>
    [Test]
    public async Task AC_BC_015_ReportValidatorRejectsRevisionTupleNumericAndSampleCorruption()
    {
        var inputs = SiteTestInputs.Read();
        var token = TestContext.Current!.Execution.CancellationToken;
        var sourcePath = Path.Combine(inputs.Reports, SiteTokens.SmallProfile, SiteTokens.ReportFile);
        var source = JsonNode.Parse(await File.ReadAllTextAsync(sourcePath, token))!.AsObject();
        var invalidReports = new List<JsonObject>
        {
            ChangeRoot(source, SiteTokens.SourceRevision, new(SiteTokens.HexZero, SiteTokens.ShaLength)),
            RemoveFirstCase(source),
            ChangeFirstLatency(source, -1d),
            RemoveFirstSample(source),
        };

        foreach (var invalid in invalidReports)
        {
            var response = await SiteNodeProbe.RunAsync(inputs, new(SiteTokens.ReportOperation,
                Value: JsonSerializer.SerializeToElement(invalid, SiteTokens.JsonOptions), ExpectedRevision: inputs.MeasuredRevision), token);
            await Assert.That(response.Accepted).IsFalse();
        }

        var malformedJson = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await SiteNodeProbe.RunRawAsync(inputs, SiteTokens.InvalidJson, token));
        await Assert.That(malformedJson).IsNotNull();
        await Assert.That(malformedJson?.Message.Contains(SiteTokens.StandardErrorLabel, StringComparison.Ordinal) == true).IsTrue();
        await Assert.That(malformedJson?.Message.Contains(SiteBuilderTokens.JsonParserMarker, StringComparison.Ordinal) == true).IsTrue();
        await Assert.That(malformedJson?.Message.Contains(SiteBuilderTokens.VendorError, StringComparison.Ordinal) == false).IsTrue();
    }

    private static JsonObject ChangeEntry(JsonObject catalog, string property, string value)
    {
        var changed = (JsonObject)catalog.DeepClone();
        changed[SiteTokens.Runs]!.AsArray()[SiteTokens.Zero]![property] = value;
        return changed;
    }

    private static JsonObject RemoveEntryField(JsonObject catalog, string property)
    {
        var changed = (JsonObject)catalog.DeepClone();
        changed[SiteTokens.Runs]!.AsArray()[SiteTokens.Zero]!.AsObject().Remove(property);
        return changed;
    }

    private static JsonObject DuplicateFirstRun(JsonObject catalog)
    {
        var changed = (JsonObject)catalog.DeepClone();
        var runs = changed[SiteTokens.Runs]!.AsArray();
        runs[SiteTokens.One]![SiteTokens.Id] = runs[SiteTokens.Zero]![SiteTokens.Id]!.GetValue<string>();
        return changed;
    }

    private static JsonObject ChangeRoot(JsonObject report, string property, string value)
    {
        var changed = (JsonObject)report.DeepClone();
        changed[property] = value;
        return changed;
    }

    private static JsonObject RemoveFirstCase(JsonObject report)
    {
        var changed = (JsonObject)report.DeepClone();
        changed[SiteTokens.Cases]!.AsArray().RemoveAt(SiteTokens.Zero);
        return changed;
    }

    private static JsonObject ChangeFirstLatency(JsonObject report, double value)
    {
        var changed = (JsonObject)report.DeepClone();
        changed[SiteTokens.Cases]!.AsArray()[SiteTokens.Zero]![SiteTokens.Measurement]![SiteTokens.Latency]![SiteTokens.P50] = value;
        return changed;
    }

    private static JsonObject RemoveFirstSample(JsonObject report)
    {
        var changed = (JsonObject)report.DeepClone();
        changed[SiteTokens.Cases]!.AsArray()[SiteTokens.Zero]![SiteTokens.Samples]!.AsArray().RemoveAt(SiteTokens.Zero);
        return changed;
    }
}
