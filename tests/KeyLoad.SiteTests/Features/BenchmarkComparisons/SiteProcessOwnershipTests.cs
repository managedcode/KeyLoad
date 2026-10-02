using System.Net;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteProcessOwnershipTests
{
    private const string FileName = "ownership.txt";
    private const string FileContents = "static-listener-owned-content";

    [Test]
    public async Task AcCq013RealStaticListenerServesAndClosesBeforeRepeatedDisposal()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var directory = SiteTempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, FileName), FileContents, token);
        await using var host = SiteStaticFileHost.Start(directory.Path);
        using var client = new HttpClient();
        var endpoint = new Uri(new Uri(host.BaseUrl), FileName);
        using (var response = await client.GetAsync(endpoint, token))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsStringAsync(token)).IsEqualTo(FileContents);
        }

        await host.DisposeAsync();
        await host.DisposeAsync();

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
        {
            using var unexpected = await client.GetAsync(endpoint, token);
        });
    }

    [Test]
    public void AcCq013InvalidRootFailsWithoutPublishingListener()
        => Assert.ThrowsExactly<ArgumentException>(() => SiteStaticFileHost.Start(string.Empty));
}
