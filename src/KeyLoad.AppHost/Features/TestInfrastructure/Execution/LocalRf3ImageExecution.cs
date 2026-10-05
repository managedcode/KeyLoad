namespace KeyLoad.AppHost.Features.TestInfrastructure.Execution;

internal sealed record LocalRf3ImageExecution(string Root, string Tag, string ReceiptPath)
{
    private const string GetImageReferenceComparisonText = ":";

    private const string ImageTagIdentityFormat = "N";

    internal const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    internal const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    internal const string ImageReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    internal const string Provenance = "local-development";
    internal const string Repository = "keyload/local-server";
    internal string ImageReference => Repository + GetImageReferenceComparisonText + Tag;

    internal static LocalRf3ImageExecution Create(string root)
    {
        const string ComparisonText = "local-";
        const string Path1Text = "TestResults";
        const string Path2Text = "rf3";
        const string Path3Text = "local-images";
        const string Path4Text = "image-";
        const string ResultText = "local-";
        const string CreatePath4Text = ".json";

        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var tag = ComparisonText + Guid.NewGuid().ToString(ImageTagIdentityFormat);
        var receiptPath = Path.Combine(Path1Text, Path2Text, Path3Text, Path4Text
            + tag[ResultText.Length..] + CreatePath4Text);
        return new(Path.GetFullPath(root), tag, receiptPath);
    }
}
