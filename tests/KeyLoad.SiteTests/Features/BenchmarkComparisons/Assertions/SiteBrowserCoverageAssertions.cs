namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-024/025/028: independently reads genuine Node and Chrome receipts through the final converter.</summary>
internal static class SiteBrowserCoverageAssertions
{
    public static async Task AssertNativeConversionAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        await AssertAuthenticSessionAsync(baseUrl, inputs, cancellationToken);
        await AssertEmptyOnlySessionRejectedAsync(baseUrl, inputs, cancellationToken);
        await AssertAnonymousOnlySessionRejectedAsync(baseUrl, inputs, cancellationToken);
        await AssertMalformedSessionRejectedAsync(baseUrl, inputs, missingResult: true, cancellationToken);
        await AssertMalformedSessionRejectedAsync(baseUrl, inputs, missingResult: false, cancellationToken);
        await AssertForeignOriginRejectedAsync(baseUrl, inputs, cancellationToken);
        await AssertEmptyNodeRejectedAsync(baseUrl, inputs, cancellationToken);
    }

    private static async Task AssertAuthenticSessionAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        using var fixture = await SiteBrowserCoverageFixture.CreateAsync(baseUrl, inputs, cancellationToken);
        var actual = await ReadAsync(fixture, inputs, cancellationToken);
        await Assert.That(actual.Errors.Count).IsEqualTo(SiteCoverageTokens.Zero);
        await Assert.That(actual.BrowserSessions.Count).IsEqualTo(SiteCoverageTokens.One);
        await Assert.That(actual.Receipts.Count).IsEqualTo(fixture.BrowserFiles.Count + SiteCoverageTokens.One);
        var browser = actual.BrowserSessions.Single();
        await Assert.That(browser.Id).IsEqualTo(fixture.SessionId);
        await Assert.That(browser.MetadataPath).IsEqualTo(fixture.MetadataPath(fixture.SessionId));
        await Assert.That(browser.MetadataSha256).IsEqualTo(SiteCoverageSourceManifestWriter.Hash(fixture.MetadataBytes));
        await Assert.That(browser.Origins.Contains(baseUrl.TrimEnd(SiteTokens.UrlPathSeparatorCharacter),
            StringComparer.Ordinal)).IsTrue();
        var expectedPaths = fixture.BrowserFiles.Keys.Select(fixture.BrowserReceiptPath)
            .ToHashSet(StringComparer.Ordinal);
        await Assert.That(browser.ReceiptPaths.ToHashSet(StringComparer.Ordinal).SetEquals(expectedPaths)).IsTrue();
        foreach (var file in fixture.BrowserFiles)
        {
            var receipt = actual.Receipts.Single(value => value.Path == fixture.BrowserReceiptPath(file.Key));
            await Assert.That(receipt.Runtime).IsEqualTo(SiteCoverageTokens.BrowserRuntime);
            await Assert.That(receipt.Sha256).IsEqualTo(SiteCoverageSourceManifestWriter.Hash(file.Value));
            await Assert.That(receipt.RuntimeVersion).IsEqualTo(browser.Version);
        }

        var node = actual.Receipts.Single(receipt => receipt.Path == fixture.NodeReceiptPath);
        await Assert.That(node.Runtime).IsEqualTo(SiteCoverageTokens.NodeRuntime);
        await Assert.That(node.RuntimeVersion).IsEqualTo(fixture.Manifest.NodeVersion);
        await Assert.That(node.Sha256).IsEqualTo(SiteCoverageSourceManifestWriter.Hash(fixture.NodeBytes));
        await Assert.That(node.MappedFunctions > SiteCoverageTokens.Zero).IsTrue();
        await Assert.That(actual.Receipts.Where(receipt => receipt.Runtime == SiteCoverageTokens.BrowserRuntime)
            .Sum(receipt => receipt.MappedFunctions) > SiteCoverageTokens.Zero).IsTrue();
        var empty = fixture.EmptyBrowserFile;
        var emptyPath = fixture.BrowserReceiptPath(empty.Name);
        await Assert.That(actual.Receipts.Single(receipt => receipt.Path == emptyPath).MappedFunctions)
            .IsEqualTo(SiteCoverageTokens.Zero);
        var emptySnapshots = actual.SnapshotsBySource.Values.SelectMany(snapshots => snapshots)
            .Where(snapshot => snapshot.ReceiptIdentity == emptyPath).ToArray();
        await Assert.That(emptySnapshots.Length).IsEqualTo(SiteCoverageTokens.Zero);
        foreach (var source in fixture.Manifest.Sources)
        {
            var metrics = SiteCoverageAnalyzer.Analyze(source, inputs.Repository, emptySnapshots);
            await Assert.That(metrics.CoveredLines).IsEqualTo(SiteCoverageTokens.Zero);
            await Assert.That(metrics.CoveredBlockOutcomes).IsEqualTo(SiteCoverageTokens.Zero);
        }
    }

    private static async Task AssertEmptyOnlySessionRejectedAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        using var fixture = await SiteBrowserCoverageFixture.CreateAsync(baseUrl, inputs, cancellationToken);
        var id = fixture.AddSecondSession(fixture.EmptyBrowserFile.Bytes);
        var actual = await ReadAsync(fixture, inputs, cancellationToken);
        await AssertValidSessionSurvivesAsync(actual, fixture);
        await AssertBadSessionErrorAsync(actual, fixture, id, SiteCoverageTokens.MissingBrowserSessionRangesFailure);
    }

    private static async Task AssertAnonymousOnlySessionRejectedAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        using var fixture = await SiteBrowserCoverageFixture.CreateAsync(baseUrl, inputs, cancellationToken);
        var id = fixture.AddSecondSession(fixture.AnonymousOnlyBytes());
        var actual = await ReadAsync(fixture, inputs, cancellationToken);
        await AssertValidSessionSurvivesAsync(actual, fixture);
        await AssertBadSessionErrorAsync(actual, fixture, id, SiteCoverageTokens.MissingBrowserSessionRangesFailure);
    }

    private static async Task AssertMalformedSessionRejectedAsync(string baseUrl, SiteTestInputs inputs,
        bool missingResult, CancellationToken cancellationToken)
    {
        using var fixture = await SiteBrowserCoverageFixture.CreateAsync(baseUrl, inputs, cancellationToken);
        var bytes = fixture.ModifiedBrowserBytes(root =>
        {
            if (missingResult)
            {
                root.Remove(SiteCoverageTokens.Result);
            }
            else
            {
                root[SiteCoverageTokens.Result] = SiteBrowserCoverageTokens.InvalidResult;
            }
        });
        var id = fixture.AddSecondSession(bytes);
        var actual = await ReadAsync(fixture, inputs, cancellationToken);
        await AssertValidSessionSurvivesAsync(actual, fixture);
        await AssertBadReceiptErrorAsync(actual, fixture, id, missingResult ? SiteCoverageTokens.JsonFailure :
            SiteCoverageTokens.EmptyNativeFileFailure);
    }

    private static async Task AssertForeignOriginRejectedAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        using var fixture = await SiteBrowserCoverageFixture.CreateAsync(baseUrl, inputs, cancellationToken);
        var bytes = fixture.ModifiedBrowserBytes(root =>
        {
            var script = root[SiteCoverageTokens.Result]!.AsArray().First(item =>
                item?[SiteCoverageTokens.Url]?.GetValue<string>().StartsWith(
                    baseUrl.TrimEnd(SiteTokens.UrlPathSeparatorCharacter) + SiteCoverageTokens.FeatureUrlPrefix,
                    StringComparison.Ordinal) == true)!.AsObject();
            var original = new Uri(script[SiteCoverageTokens.Url]!.GetValue<string>());
            script[SiteCoverageTokens.Url] = SiteBrowserCoverageTokens.ForeignOrigin + original.AbsolutePath;
        });
        var id = fixture.AddSecondSession(bytes);
        var actual = await ReadAsync(fixture, inputs, cancellationToken);
        await AssertValidSessionSurvivesAsync(actual, fixture);
        await AssertBadReceiptErrorAsync(actual, fixture, id, SiteCoverageTokens.BrowserOriginFailure);
    }

    private static async Task AssertEmptyNodeRejectedAsync(string baseUrl, SiteTestInputs inputs,
        CancellationToken cancellationToken)
    {
        using var fixture = await SiteBrowserCoverageFixture.CreateAsync(baseUrl, inputs, cancellationToken);
        fixture.ReplaceNodeWithEmptyResult();
        var actual = await ReadAsync(fixture, inputs, cancellationToken);
        await Assert.That(actual.Errors.Any(error => error.Contains(fixture.NodeReceiptPath, StringComparison.Ordinal) &&
            error.Contains(SiteCoverageTokens.EmptyNativeFileFailure, StringComparison.Ordinal))).IsTrue();
        await Assert.That(actual.Receipts.Any(receipt => receipt.Runtime == SiteCoverageTokens.BrowserRuntime &&
            receipt.MappedFunctions > SiteCoverageTokens.Zero)).IsTrue();
    }

    private static Task<SiteCoverageCollection> ReadAsync(SiteBrowserCoverageFixture fixture, SiteTestInputs inputs,
        CancellationToken cancellationToken) => SiteCoverageArtifactReader.ReadAsync(inputs.Repository, fixture.Root,
        fixture.Manifest, cancellationToken);

    private static async Task AssertValidSessionSurvivesAsync(SiteCoverageCollection actual,
        SiteBrowserCoverageFixture fixture)
    {
        await Assert.That(actual.BrowserSessions.Any(session => session.Id == fixture.SessionId &&
            session.ReceiptPaths.Count == fixture.BrowserFiles.Count)).IsTrue();
        await Assert.That(actual.Receipts.Any(receipt => receipt.Runtime == SiteCoverageTokens.BrowserRuntime &&
            receipt.MappedFunctions > SiteCoverageTokens.Zero)).IsTrue();
        await Assert.That(actual.Receipts.Any(receipt => receipt.Path == fixture.NodeReceiptPath &&
            receipt.MappedFunctions > SiteCoverageTokens.Zero)).IsTrue();
    }

    private static async Task AssertBadSessionErrorAsync(SiteCoverageCollection actual,
        SiteBrowserCoverageFixture fixture, string id, string message)
    {
        await Assert.That(actual.Errors.Any(error => error.Contains(fixture.MetadataPath(id), StringComparison.Ordinal) &&
            error.Contains(message, StringComparison.Ordinal))).IsTrue();
    }

    private static async Task AssertBadReceiptErrorAsync(SiteCoverageCollection actual,
        SiteBrowserCoverageFixture fixture, string id, string message)
    {
        var path = fixture.BrowserReceiptPathFor(id, SiteBrowserCoverageTokens.RejectionFile);
        await Assert.That(actual.Errors.Any(error => error.Contains(path, StringComparison.Ordinal) &&
            error.Contains(message, StringComparison.Ordinal))).IsTrue();
    }
}
