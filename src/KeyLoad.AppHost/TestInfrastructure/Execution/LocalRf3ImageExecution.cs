namespace KeyLoad.AppHost.Features.TestInfrastructure.Execution;

internal sealed record LocalRf3ImageExecution(string Root, string Tag, string ReceiptPath)
{
    private const string ImageTagSeparator = ":";

    private const string ImageTagIdentityFormat = "N";

    internal const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    internal const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    internal const string ImageReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    internal const string Provenance = "local-development";
    internal const string Repository = "keyload/local-server";
    internal string ImageReference => Repository + ImageTagSeparator + Tag;

    internal static LocalRf3ImageExecution Create(string root)
    {
        const string LocalImageTagPrefix = "local-";
        const string TestResultsDirectory = "TestResults";
        const string Rf3SuiteDirectory = "rf3";
        const string LocalImagesDirectory = "local-images";
        const string ImageReceiptFilePrefix = "image-";
        const string ImageReceiptTagPrefix = "local-";
        const string JsonReceiptFileExtension = ".json";

        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var tag = LocalImageTagPrefix + Guid.NewGuid().ToString(ImageTagIdentityFormat);
        var receiptPath = Path.Combine(TestResultsDirectory, Rf3SuiteDirectory, LocalImagesDirectory, ImageReceiptFilePrefix
            + tag[ImageReceiptTagPrefix.Length..] + JsonReceiptFileExtension);
        return new(Path.GetFullPath(root), tag, receiptPath);
    }
}
