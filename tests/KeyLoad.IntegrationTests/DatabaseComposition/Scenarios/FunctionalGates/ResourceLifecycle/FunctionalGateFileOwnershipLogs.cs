using System.Text;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;
using P = KeyLoad.IntegrationTests.Features.DatabaseComposition.FunctionalGateFileOwnershipProtocol;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Retains the original bounded native refusal prefix while separating batch traversal from stream ownership.</summary>
internal static class FunctionalGateFileOwnershipLogs
{
    internal static async Task<string> ReadAsync(FunctionalGateFixture fixture, string name, CancellationToken token)
    {
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(fixture.Root);
        string[] secrets = [profile.AdminKey, profile.SigningKey, profile.PeerSecret];
        var output = new StringBuilder();
        var logs = fixture.App.Services.GetRequiredService<ResourceLoggerService>();
        try
        {
            await foreach (var batch in logs.WatchAsync(name).WithCancellation(token).ConfigureAwait(false))
            {
                var refusal = ReadBatch(batch, secrets, output);
                if (refusal is not null)
                {
                    return refusal;
                }
            }
        }
        catch (Exception failure)
        {
            failure.Data[P.LogEvidenceKey] = output.ToString();
            throw;
        }
        throw new InvalidDataException(P.Incomplete);
    }

    private static string? ReadBatch(IEnumerable<LogLine> batch, string[] secrets, StringBuilder output)
    {
        foreach (var line in batch)
        {
            var text = line.Content;
            foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
            {
                text = text.Replace(secret, P.Redacted, StringComparison.Ordinal);
            }
            if (text.Length + P.LogLineEnding.Length > P.MaximumLogCharacters - output.Length)
            {
                var failure = new InvalidDataException(P.Incomplete);
                failure.Data[P.LogEvidenceKey] = output.ToString();
                throw failure;
            }
            output.Append(text).Append(P.LogLineEndingCharacter);
            if (FunctionalGateFileOwnershipAssertions.HasOriginalRefusal(output.ToString()))
            {
                return output.ToString();
            }
        }
        return null;
    }
}
