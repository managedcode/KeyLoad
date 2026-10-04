using System.Text.RegularExpressions;

namespace KeyLoad.AppHost.Features.ClusterReplication;

internal sealed partial record RuntimeContainerImage(string Image, string Tag, string Digest)
{
    internal const string ServerConfiguration = "KeyLoad:ContainerImages:Server";
    private const string MissingPrefix = "Missing container image setting: ";
    private const string InvalidImage = "ContainerImageReferenceInvalid";
    private const string ImageGroup = "image";
    private const string RegistryGroup = "registry";
    private const string TagGroup = "tag";
    private const string DigestGroup = "digest";
    private const string TagSeparator = ":";
    private const string DigestSeparator = "@sha256:";
    private const int MaximumImageCharacters = 1_024;
    private const int RegexTimeoutMilliseconds = 100;
    private const string ImagePattern =
        "\\A(?<image>(?<registry>[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?(?::[0-9]{1,5})?)" +
        "(?:/[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*)+):" +
        "(?<tag>[A-Za-z0-9_][A-Za-z0-9_.-]{0,127})@sha256:(?<digest>[a-f0-9]{64})\\z";

    internal string Reference => Image + TagSeparator + Tag + DigestSeparator + Digest;

    internal static RuntimeContainerImage Read(IDistributedApplicationBuilder builder, string configurationKey)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var value = builder.Configuration[configurationKey]
            ?? throw new InvalidOperationException(MissingPrefix + configurationKey);
        if (value.Length > MaximumImageCharacters)
        {
            throw new InvalidOperationException(InvalidImage);
        }
        var match = ImageReference().Match(value);
        if (!match.Success || !Uri.TryCreate(Uri.UriSchemeHttp + Uri.SchemeDelimiter +
                match.Groups[RegistryGroup].Value, UriKind.Absolute, out var registry)
            || !string.IsNullOrEmpty(registry.UserInfo))
        {
            throw new InvalidOperationException(InvalidImage);
        }
        return new(match.Groups[ImageGroup].Value, match.Groups[TagGroup].Value, match.Groups[DigestGroup].Value);
    }

    internal IResourceBuilder<ContainerResource> Add(IDistributedApplicationBuilder builder, string name)
        => builder.AddContainer(name, Image, Tag).WithImageSHA256(Digest);

    [GeneratedRegex(ImagePattern, RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex ImageReference();
}
