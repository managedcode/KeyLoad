namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedBuildRejectionTests
{
    /// <summary>AC-ISO-008: genuine native inputs cannot authorize unsafe or destructive output placement.</summary>
    [Test]
    [Arguments("existing")]
    [Arguments("inputChild")]
    [Arguments("sourceChild")]
    [Arguments("inputParent")]
    [Arguments("symlinkInput")]
    [Arguments("symlinkParent")]
    public async Task AC_ISO_008_StandaloneBuildRefusesUnsafePathsWithoutChangingInput(string condition)
    {
        var fixture = await SiteIsolatedFixture.ReadAsync();
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var temporary = SiteTempDirectory.Create();
        var manifestPath = Path.Combine(fixture.Inputs.Aggregate, "aggregate.json");
        var before = await File.ReadAllBytesAsync(manifestPath, token);
        var marker = Path.Combine(temporary.Path, "marker.txt");
        await File.WriteAllTextAsync(marker, "preserve-owned-marker", token);
        var (input, output) = Paths(fixture, temporary, condition);
        var existed = Directory.Exists(output);
        var result = await SiteIsolatedBuilderProcess.RunAsync(fixture, output, token, input);
        await Assert.That(result.ExitCode != 0).IsTrue();
        await Assert.That(result.StandardError.Length > 0).IsTrue();
        await Assert.That(Directory.Exists(output)).IsEqualTo(existed);
        await Assert.That((await File.ReadAllBytesAsync(manifestPath, token)).AsSpan().SequenceEqual(before)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(marker, token)).IsEqualTo("preserve-owned-marker");
    }

    private static (string Input, string Output) Paths(SiteIsolatedFixture fixture, SiteTempDirectory temporary, string condition)
    {
        var input = fixture.Inputs.Aggregate;
        var output = temporary.Output;
        switch (condition)
        {
            case "existing":
                output = temporary.Path;
                break;
            case "inputChild":
                output = Path.Combine(input, "isolated-test-" + Guid.NewGuid().ToString("N"));
                break;
            case "sourceChild":
                output = Path.Combine(fixture.Inputs.Site.Repository, SiteAssetTokens.SiteRootDirectory,
                    "isolated-test-" + Guid.NewGuid().ToString("N"));
                break;
            case "inputParent":
                output = Directory.GetParent(input)!.FullName;
                break;
            case "symlinkInput":
                input = Path.Combine(temporary.Path, "linked-input");
                Directory.CreateSymbolicLink(input, fixture.Inputs.Aggregate);
                break;
            case "symlinkParent":
                var target = Path.Combine(temporary.Path, "target");
                Directory.CreateDirectory(target);
                var link = Path.Combine(temporary.Path, "linked-parent");
                Directory.CreateSymbolicLink(link, target);
                output = Path.Combine(link, "output");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(condition));
        }
        return (input, output);
    }
}
