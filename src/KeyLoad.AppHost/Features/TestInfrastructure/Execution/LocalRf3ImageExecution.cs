namespace KeyLoad.AppHost.Features.TestInfrastructure.Execution;

internal sealed record LocalRf3ImageExecution(string Root, string Tag, string ReceiptPath)
{
    private const string ImageTagIdentityFormat = "N";

    internal const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    internal const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    internal const string ImageReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    internal const string Provenance = "local-development";
    internal const string Repository = "keyload/local-server";
    internal string ImageReference => Repository + ":" + Tag;

    internal static LocalRf3ImageExecution Create(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var tag = "local-" + Guid.NewGuid().ToString(ImageTagIdentityFormat);
        var receiptPath = Path.Combine("TestResults", "rf3", "local-images", "image-"
            + tag["local-".Length..] + ".json");
        return new(Path.GetFullPath(root), tag, receiptPath);
    }
}
