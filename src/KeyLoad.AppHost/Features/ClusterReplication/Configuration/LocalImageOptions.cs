using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Explicit local image selectors admitted by the native startup boundary.</summary>
[ConfigurationOptions]
internal sealed class LocalImageOptions
{
    private const string ProvenanceKey = "KEYLOAD_IMAGE_PROVENANCE";
    private const string ReceiptKey = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string ChildKey = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
    private const string GithubReceiptKey = "KEYLOAD_IMAGE_RECEIPT";
    private const string GithubRevisionKey = "GITHUB_SHA";
    private const string GithubActionsKey = "GITHUB_ACTIONS";
    [ConfigurationKeyName(ProvenanceKey)] public string? Provenance { get; set; }
    [ConfigurationKeyName(ReceiptKey)] public string? ReceiptPath { get; set; }
    [ConfigurationKeyName(ChildKey)] public string? Child { get; set; }
    [ConfigurationKeyName(GithubReceiptKey)] public string? GithubReceipt { get; set; }
    [ConfigurationKeyName(GithubRevisionKey)] public string? GithubRevision { get; set; }
    [ConfigurationKeyName(GithubActionsKey)] public string? GithubActions { get; set; }
}
