namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed record ClusterFixtureSourceImage(string Reference, string ManifestDigest, string ReceiptSha256);
