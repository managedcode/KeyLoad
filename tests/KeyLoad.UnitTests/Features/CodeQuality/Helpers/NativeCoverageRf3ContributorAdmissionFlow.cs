using System.Text.Json.Nodes;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

/// <summary>Exercises the actual admission reader against the genuine prepared source manifest.</summary>
internal static class NativeCoverageRf3ContributorAdmissionFlow
{
    private const int ExpectedContributors = 11;
    private const string Contributors = "contributors";
    private const string Suite = "suite";
    private const string Rf3 = "rf3";
    private const string Method = "methodName";
    private const string Instance = "instanceName";
    private const string Class = "className";
    private const string ForeignIdentity = "NotAnAdmittedContributor";

    internal static async Task VerifyAsync(string path, CancellationToken token)
    {
        var original = await File.ReadAllBytesAsync(path, token);
        var options = Options.Create(new NativeCoverageExecutionOptions());
        var accepted = await NativeCoverageRf3ManifestReader.ReadAsync(path, options, token);
        await Assert.That(accepted.Contributors.Count).IsEqualTo(ExpectedContributors);
        await NativeCoverageRf3SelectionAdmissionFlow.VerifyAsync(path, accepted, token);
        foreach (var mutation in new[] { Contributors, Method, Instance, Class, Rf3 })
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => RejectMutationAsync(path, original, mutation, options, token), failures);
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(path, original, CancellationToken.None), failures);
            global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
            var healthy = await NativeCoverageRf3ManifestReader.ReadAsync(path, options, token);
            await Assert.That(healthy.SourceManifestSha256).IsEqualTo(accepted.SourceManifestSha256);
            await Assert.That(healthy.Contributors.SequenceEqual(accepted.Contributors)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(original)).IsTrue();
        }
    }

    private static async Task RejectMutationAsync(string path, byte[] original, string mutation,
        IOptions<NativeCoverageExecutionOptions> options, CancellationToken token)
    {
        var root = JsonNode.Parse(original)!.AsObject();
        var rows = root[Contributors]!.AsArray();
        var last = rows.Last(row => row![Suite]!.GetValue<string>() == Rf3)!;
        Change(rows, last, mutation);
        await File.WriteAllTextAsync(path, root.ToJsonString(), token);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            NativeCoverageRf3ManifestReader.ReadAsync(path, options, token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.Message).IsEqualTo(NativeCoverageRf3Protocol.InvalidManifest);
    }

    private static void Change(JsonArray rows, JsonNode last, string mutation)
    {
        if (mutation == Contributors)
        {
            rows.Remove(last);
        }
        else if (mutation == Rf3)
        {
            rows.Add(last.DeepClone());
        }
        else
        {
            last[mutation] = ForeignIdentity;
        }
    }
}
