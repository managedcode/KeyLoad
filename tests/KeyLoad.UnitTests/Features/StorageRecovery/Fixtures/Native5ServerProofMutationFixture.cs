using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static KeyLoad.UnitTests.Features.StorageRecovery.Native5ServerProofFixture;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class Native5ServerProofMutationFixture
{
    internal static Native5ServerParserCase ChangeReceipt(Native5ServerParserCase source, string name,
        Action<JsonObject> change)
    {
        var receipt = JsonNode.Parse(source.Receipt)!.AsObject();
        change(receipt);
        return source with { Name = name, Receipt = receipt.ToJsonString() };
    }

    internal static Native5ServerParserCase ChangeReceiptSection(Native5ServerParserCase source, string name,
        string sectionName, Action<JsonObject> change)
        => ChangeReceipt(source, name, receipt => change(receipt[sectionName]!.AsObject()));

    internal static Native5ServerParserCase ChangeManifest(Native5ServerParserCase source, string name,
        Action<JsonObject> change)
    {
        var manifest = JsonNode.Parse(source.Manifest)!.AsObject();
        change(manifest);
        var responseType = JsonNode.Parse(source.Receipt)![Image]![ContentType]!.GetValue<string>();
        return RebindManifest(source with { Name = name }, manifest, responseType);
    }

    internal static Native5ServerParserCase WithRawReceipt(Native5ServerParserCase source, string name, string receipt)
        => source with { Name = name, Receipt = receipt };

    internal static Native5ServerParserCase WithRawManifest(Native5ServerParserCase source, string name, string manifest)
        => source with { Name = name, Manifest = manifest };

    internal static Native5ServerParserCase WithRawManifestBound(Native5ServerParserCase source, string name,
        string manifest)
    {
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)));
        var receipt = JsonNode.Parse(source.Receipt)!.AsObject();
        receipt[Image]![ManifestSha256] = digest;
        receipt[Image]![RegistryDigest] = digest;
        var reference = receipt[Image]![TaggedReference]!.GetValue<string>() + DigestSeparator + digest;
        receipt[Image]![Reference] = reference;
        return source with
        {
            Name = name,
            Manifest = manifest,
            Receipt = receipt.ToJsonString(),
            ExpectedReference = reference
        };
    }

    internal static Native5ServerParserCase OversizedManifest(Native5ServerParserCase source)
        => source with { Name = "manifest exceeds byte cap", OversizedManifest = true };

    internal static Native5ServerParserCase ArchiveObservation(Native5ServerParserCase source)
        => ChangeReceiptSection(source, "valid alternate archive observation", ImageSource, item =>
        {
            item[ArchiveSha256] = new string('c', 64);
            item[ArchiveBytes] = 1;
        });

    internal static Native5ServerParserCase OptionalManifestMediaType(Native5ServerParserCase source)
        => ChangeManifest(source, "valid optional root media type", item => item.Remove(MediaType));

    internal static Native5ServerParserCase DockerManifest()
    {
        var source = Valid();
        var manifest = JsonNode.Parse(source.Manifest)!.AsObject();
        manifest[MediaType] = DockerManifestContentType;
        manifest[Config]![MediaType] = DockerConfigContentType;
        manifest[Layers]![0]![MediaType] = DockerLayerContentType;
        return RebindManifest(source, manifest, DockerManifestContentType);
    }

    internal static Native5ServerParserCase OciManifestWithLayerType(string layerType, string name)
    {
        var source = Valid();
        var manifest = JsonNode.Parse(source.Manifest)!.AsObject();
        manifest[Layers]![0]![MediaType] = layerType;
        return RebindManifest(source with { Name = name }, manifest, ManifestContentType);
    }

    private static Native5ServerParserCase RebindManifest(Native5ServerParserCase source, JsonObject manifest,
        string contentType)
    {
        var manifestText = manifest.ToJsonString();
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifestText)));
        var receipt = JsonNode.Parse(source.Receipt)!.AsObject();
        receipt[Image]![ContentType] = contentType;
        receipt[Image]![ManifestSha256] = digest;
        receipt[Image]![RegistryDigest] = digest;
        var reference = receipt[Image]![TaggedReference]!.GetValue<string>() + DigestSeparator + digest;
        receipt[Image]![Reference] = reference;
        var name = source.Name == "valid synthetic parser input" ? "valid Docker schema2 parser input" : source.Name;
        return source with
        {
            Name = name,
            Manifest = manifestText,
            Receipt = receipt.ToJsonString(),
            ExpectedReference = reference
        };
    }

    internal static Native5ServerParserCase AnnotationBoundary()
        => AnnotationCount(MaximumAnnotations, "valid maximum annotation count");

    internal static Native5ServerParserCase AnnotationCount(int count, string name)
        => ChangeManifest(Valid(), name, item =>
        {
            var annotations = new JsonObject();
            for (var index = 0; index < count; index++)
            { annotations[$"k{index:D3}"] = string.Empty; }
            item[Annotations] = annotations;
        });

    internal static Native5ServerParserCase AnnotationByteBoundary()
        => ChangeManifest(Valid(), "valid annotation UTF8 byte limits", item =>
            item[Annotations] = new JsonObject { [new string('k', MaximumAnnotationKeyBytes)] = new string('v', MaximumAnnotationValueBytes) });

    internal static Native5ServerParserCase WithLayerCount(Native5ServerParserCase source, int count,
        string name)
        => source with { Name = name, LayerCountOverride = count };

    internal static Native5ServerParserCase ChangeProducer(Native5ServerParserCase source, string name,
        Action<JsonObject> change)
    {
        var producer = JsonNode.Parse(source.ExpectedProducer)!.AsObject();
        change(producer);
        return source with { Name = name, ExpectedProducer = producer.ToJsonString() };
    }

    internal static Native5ServerParserCase MatchProducerChange(Native5ServerParserCase source, string name,
        Action<JsonObject> change)
    {
        var changed = ChangeProducer(source, name, change);
        return ChangeReceiptSection(changed, name, Producer, change);
    }

    internal const int MaximumAnnotations = 256;
    internal const int MaximumAnnotationKeyBytes = 256;
    internal const int MaximumAnnotationValueBytes = 4096;
}
