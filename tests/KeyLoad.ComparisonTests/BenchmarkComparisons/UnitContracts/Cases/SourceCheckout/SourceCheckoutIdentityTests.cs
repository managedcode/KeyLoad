namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class SourceCheckoutIdentityTests
{
    private const string DirtySourceMessage = "Tracked source changes are not allowed for image preparation.";

    [Test]
    public async Task CleanSourceIdentityRejectsUntrackedInputAndRecoversWithoutChangingHead()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var checkout = await TemporaryGitCheckout.CreateAsync(token);
        var trackedHash = await checkout.ReadTrackedHashAsync(token);
        var clean = await checkout.VerifySourceCheckoutAsync(token);
        await Assert.That(clean.Accepted).IsTrue();

        await checkout.CreateUntrackedSourceAsync(token);
        var rejected = await checkout.VerifySourceCheckoutAsync(token);
        await Assert.That(rejected.Accepted).IsFalse();
        await Assert.That(rejected.Message).IsEqualTo(DirtySourceMessage);
        await Assert.That(await checkout.ReadTrackedHashAsync(token)).IsEqualTo(trackedHash);

        await checkout.RemoveUntrackedSourceAsync(token);
        await Assert.That(File.Exists(checkout.UntrackedSourcePath)).IsFalse();
        var recovered = await checkout.VerifySourceCheckoutAsync(token);
        await Assert.That(recovered.Accepted).IsTrue();

        await checkout.CreateIgnoredGeneratedFileAsync(token);
        await Assert.That(File.Exists(checkout.IgnoredGeneratedFilePath)).IsTrue();
        var ignored = await checkout.VerifySourceCheckoutAsync(token);
        await Assert.That(ignored.Accepted).IsTrue();
        await Assert.That(await checkout.ReadTrackedHashAsync(token)).IsEqualTo(trackedHash);
    }
}
