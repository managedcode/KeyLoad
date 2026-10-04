using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class Native5ServerProofCaseCatalog
{
    private const string AdditionalProperty = "extra";
    private const string AnnotationTestKey = "key";

    private const string UnknownProperty = "unexpected";
    internal static IReadOnlyList<Native5ServerParserCase> ProducerFailures()
    {
        var valid = Native5ServerProofFixture.Valid();
        return
        [
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected source SHA mismatch", item => item[Native5ServerProofFixture.SourceSha] = "b" + new string('a', 39)),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected run mismatch", item => item[Native5ServerProofFixture.RunId] = "12345678901235"),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected attempt mismatch", item => item[Native5ServerProofFixture.RunAttempt] = "2"),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected repository mismatch", item => item[Native5ServerProofFixture.Repository] = "fork/KeyLoad"),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected ref mismatch", item => item[Native5ServerProofFixture.Ref] = "refs/heads/other"),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected workflow mismatch", item => item[Native5ServerProofFixture.Workflow] = "manual"),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected job mismatch", item => item[Native5ServerProofFixture.Job] = "unit-tests"),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected producer unknown field", item => item[UnknownProperty] = true),
            Native5ServerProofMutationFixture.ChangeProducer(valid, "expected producer missing field", item => item.Remove(Native5ServerProofFixture.Job)),
            Native5ServerProofMutationFixture.MatchProducerChange(valid, "producer SHA equals prior image", item => item[Native5ServerProofFixture.SourceSha] = Native5ServerProofFixture.PriorRevision),
            Native5ServerProofMutationFixture.MatchProducerChange(valid, "producer repository control string", item => item[Native5ServerProofFixture.Repository] = "managedcode/KeyLoad\n"),
            Native5ServerProofMutationFixture.MatchProducerChange(valid, "producer invalid ref", item => item[Native5ServerProofFixture.Ref] = "main"),
            Native5ServerProofMutationFixture.MatchProducerChange(valid, "producer invalid run ID", item => item[Native5ServerProofFixture.RunId] = "0"),
            ReceiptProducer(valid, Native5ServerProofFixture.SourceSha, "wrong receipt producer SHA"),
            ReceiptProducer(valid, Native5ServerProofFixture.RunId, "12345678901235", "wrong receipt run ID"),
            ReceiptProducer(valid, Native5ServerProofFixture.RunAttempt, "2", "wrong receipt run attempt"),
            ReceiptProducer(valid, Native5ServerProofFixture.Repository, "fork/KeyLoad", "wrong receipt repository"),
            ReceiptProducer(valid, Native5ServerProofFixture.Ref, "refs/heads/other", "wrong receipt ref"),
            ReceiptProducer(valid, Native5ServerProofFixture.Workflow, "manual", "wrong receipt workflow"),
            ReceiptProducer(valid, Native5ServerProofFixture.Job, "unit-tests", "wrong receipt job"),
            Native5ServerProofMutationFixture.ChangeReceiptSection(valid, "receipt producer missing job",
                Native5ServerProofFixture.Producer, item => item.Remove(Native5ServerProofFixture.Job)),
            Native5ServerProofMutationFixture.ChangeReceiptSection(valid, "receipt producer unknown field",
                Native5ServerProofFixture.Producer, item => item[UnknownProperty] = true)
        ];
    }

    internal static IReadOnlyList<Native5ServerParserCase> SourceAndBaseFailures()
    {
        var valid = Native5ServerProofFixture.Valid();
        return
        [
            ReceiptSource(valid, Native5ServerProofFixture.Revision, Native5ServerProofFixture.CurrentProducerSha, "current source relabeled as prior"),
            ReceiptSource(valid, Native5ServerProofFixture.TreeSha, new string('c', 40), "wrong prior tree"),
            ReceiptSource(valid, Native5ServerProofFixture.ArchiveFile, "../prior-server-source.tar", "archive traversal"),
            ReceiptSource(valid, Native5ServerProofFixture.InventoryFile, "source.json", "noncanonical inventory filename"),
            ReceiptSource(valid, Native5ServerProofFixture.ArchiveBytes, 0, "empty archive observation"),
            ReceiptSource(valid, Native5ServerProofFixture.ArchiveBytes, Native5ServerProofFixture.MaximumArchiveBytes + 1, "archive observation exceeds cap"),
            ReceiptSource(valid, Native5ServerProofFixture.ArchiveSha256, "not-a-digest", "malformed archive observation digest"),
            ReceiptSource(valid, Native5ServerProofFixture.SourceInventorySha256, new string('c', 64), "source transcript mismatch"),
            ReceiptSource(valid, Native5ServerProofFixture.FileCount, Native5ServerProofFixture.PriorFileCount - 1, "source file count mismatch"),
            ReceiptSource(valid, Native5ServerProofFixture.ExpandedBytes, Native5ServerProofFixture.PriorExpandedBytes + 1, "source expanded bytes mismatch"),
            ReceiptSource(valid, Native5ServerProofFixture.OverlayCount, 1, "source overlay present"),
            ReceiptSource(valid, Native5ServerProofFixture.InventoryFileSha256, "not-a-digest", "malformed inventory digest"),
            ReceiptBase(valid, Native5ServerProofFixture.Sdk, "mcr.microsoft.com/dotnet/sdk:10.0.401", "unpinned SDK base"),
            ReceiptBase(valid, Native5ServerProofFixture.Runtime, "mcr.microsoft.com/dotnet/aspnet:10.0.12", "unpinned runtime base"),
            ReceiptBase(valid, Native5ServerProofFixture.Registry, "registry:3.1.2", "unpinned registry base")
        ];
    }

    internal static IReadOnlyList<Native5ServerParserCase> ImageFailures()
    {
        var valid = Native5ServerProofFixture.Valid();
        return
        [
            ReceiptImage(valid, Native5ServerProofFixture.Name, "comparison", "wrong image name"),
            ReceiptImage(valid, Native5ServerProofFixture.TaggedReference, "127.0.0.1:5000/keyload/server:other", "wrong image tag"),
            ReceiptImage(valid, Native5ServerProofFixture.TaggedReference,
                "127.0.0.1:5000/keyload/server:" + Native5ServerProofFixture.CurrentProducerSha + "-12345678901234-1",
                "current source incorrectly used as prior image tag"),
            ReceiptImage(valid, Native5ServerProofFixture.ManifestFile, "../manifest.json", "manifest traversal"),
            ReceiptImage(valid, Native5ServerProofFixture.ManifestSha256, "sha256:" + new string('c', 64), "manifest digest mismatch"),
            ReceiptImage(valid, Native5ServerProofFixture.RegistryDigest, "sha256:" + new string('c', 64), "registry digest mismatch"),
            ReceiptImage(valid, Native5ServerProofFixture.ContentType, "application/octet-stream", "unsupported manifest type"),
            ReceiptImage(valid, Native5ServerProofFixture.ConfigId, "sha256:" + new string('c', 64), "config digest mismatch"),
            ReceiptImage(valid, Native5ServerProofFixture.RevisionLabel, Native5ServerProofFixture.CurrentProducerSha, "current revision label"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "manifest schema mismatch", item => item[Native5ServerProofFixture.SchemaVersion] = 1),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "manifest config mismatch", item => item[Native5ServerProofFixture.Config]![Native5ServerProofFixture.Digest] = "sha256:" + new string('c', 64)),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "missing manifest layers", item => item.Remove(Native5ServerProofFixture.Layers)),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "unknown manifest root field", item => item[UnknownProperty] = true),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "root media type mismatch", item => item[Native5ServerProofFixture.MediaType] = "application/vnd.oci.image.index.v1+json"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "manifest annotations wrong type", item => item[Native5ServerProofFixture.Annotations] = new JsonArray()),
            Native5ServerProofMutationFixture.AnnotationCount(Native5ServerProofMutationFixture.MaximumAnnotations + 1, "annotation count above profile limit"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "annotation value wrong type", item => item[Native5ServerProofFixture.Annotations]![AnnotationTestKey] = 1),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "annotation key too long in UTF8", item => item[Native5ServerProofFixture.Annotations] = new JsonObject { [new string('é', 129)] = "" }),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "annotation value too long in UTF8", item => item[Native5ServerProofFixture.Annotations] = new JsonObject { [AnnotationTestKey] = new string('é', 2049) }),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "empty annotation key", item => item[Native5ServerProofFixture.Annotations] = new JsonObject { [""] = "value" }),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "empty layer list", item => item[Native5ServerProofFixture.Layers] = new JsonArray()),
            Native5ServerProofMutationFixture.WithLayerCount(valid, 1025, "layer count above profile limit"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "artifact manifest", item => item[Native5ServerProofFixture.ArtifactType] = "application/example.artifact"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "subject manifest", item => item[Native5ServerProofFixture.Subject] = new JsonObject()),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "image index form", item => item[Native5ServerProofFixture.Manifests] = new JsonArray()),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "config descriptor missing media type", item => item[Native5ServerProofFixture.Config]!.AsObject().Remove(Native5ServerProofFixture.MediaType)),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "config descriptor invalid size", item => item[Native5ServerProofFixture.Config]![Native5ServerProofFixture.Size] = -1),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "config descriptor unsafe size", item => item[Native5ServerProofFixture.Config]![Native5ServerProofFixture.Size] = 9007199254740992L),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "config descriptor invalid digest", item => item[Native5ServerProofFixture.Config]![Native5ServerProofFixture.Digest] = "sha256:ABC"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "config descriptor unknown field", item => item[Native5ServerProofFixture.Config]![UnknownProperty] = true),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "layer descriptor missing digest", item => item[Native5ServerProofFixture.Layers]![0]!.AsObject().Remove(Native5ServerProofFixture.Digest)),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "layer descriptor invalid size type", item => item[Native5ServerProofFixture.Layers]![0]![Native5ServerProofFixture.Size] = "1"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "layer invalid digest", item => item[Native5ServerProofFixture.Layers]![0]![Native5ServerProofFixture.Digest] = "sha256:ABC"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "layer unsupported foreign type", item => item[Native5ServerProofFixture.Layers]![0]![Native5ServerProofFixture.MediaType] = "application/vnd.oci.image.layer.nondistributable.v1.tar+gzip"),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "layer foreign URL", item => item[Native5ServerProofFixture.Layers]![0]![Native5ServerProofFixture.Urls] = new JsonArray(JsonValue.Create("https://example.invalid/layer"))),
            Native5ServerProofMutationFixture.ChangeManifest(valid, "layer embedded data", item => item[Native5ServerProofFixture.Layers]![0]![Native5ServerProofFixture.Data] = "AA=="),
            Native5ServerProofMutationFixture.WithRawManifest(valid, "manifest malformed JSON", "{"),
            Native5ServerProofMutationFixture.ChangeReceipt(valid, "expected image reference mismatch", item => item[Native5ServerProofFixture.Image]![Native5ServerProofFixture.Reference] = "127.0.0.1:5000/keyload/server:wrong")
        ];
    }

    internal static IReadOnlyList<Native5ServerParserCase> ShapeFailures()
    {
        var valid = Native5ServerProofFixture.Valid();
        var duplicateReceipt = valid.Receipt.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schemaVersion\":1,", StringComparison.Ordinal);
        var escapedReceipt = valid.Receipt.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schema\\u0056ersion\":1,", StringComparison.Ordinal);
        var duplicateManifest = valid.Manifest.Replace("\"schemaVersion\":2,", "\"schemaVersion\":2,\"schemaVersion\":2,", StringComparison.Ordinal);
        var escapedManifest = valid.Manifest.Replace("\"schemaVersion\":2,", "\"schemaVersion\":2,\"schema\\u0056ersion\":2,", StringComparison.Ordinal);
        var escapedDescriptor = valid.Manifest.Replace(
            $"\"digest\":\"{Native5ServerProofFixture.ImageConfigDigest}\"",
            $"\"digest\":\"{Native5ServerProofFixture.ImageConfigDigest}\",\"dig\\u0065st\":\"{Native5ServerProofFixture.ImageConfigDigest}\"",
            StringComparison.Ordinal);
        var duplicateLayerDigest = valid.Manifest.Replace(
            $"\"digest\":\"{Native5ServerProofFixture.LayerDigest}\"",
            $"\"digest\":\"{Native5ServerProofFixture.LayerDigest}\",\"digest\":\"{Native5ServerProofFixture.LayerDigest}\"",
            StringComparison.Ordinal);
        const string AnnotationField = "\"" + Native5ServerProofFixture.TitleAnnotationKey + "\":\""
            + Native5ServerProofFixture.TitleAnnotationValue + "\"";
        var duplicateAnnotation = valid.Manifest.Replace(AnnotationField,
            AnnotationField + "," + AnnotationField, StringComparison.Ordinal);
        var escapedAnnotation = valid.Manifest.Replace(AnnotationField,
            AnnotationField + ",\"org.opencontainers.image.ti\\u0074le\":\""
            + Native5ServerProofFixture.TitleAnnotationValue + "\"",
            StringComparison.Ordinal);
        var descriptorAnnotations = Native5ServerProofMutationFixture.ChangeManifest(valid,
            "descriptor annotation duplicate input", item =>
                item[Native5ServerProofFixture.Config]![Native5ServerProofFixture.Annotations] = new JsonObject
                { [Native5ServerProofFixture.TitleAnnotationKey] = Native5ServerProofFixture.TitleAnnotationValue });
        var duplicateDescriptorAnnotation = descriptorAnnotations.Manifest.Replace(AnnotationField,
            AnnotationField + "," + AnnotationField, StringComparison.Ordinal);
        return
        [
            Native5ServerProofMutationFixture.ChangeReceipt(valid, "unknown top-level field", item => item[AdditionalProperty] = true),
            Native5ServerProofMutationFixture.ChangeReceipt(valid, "missing image source", item => item.Remove(Native5ServerProofFixture.ImageSource)),
            Native5ServerProofMutationFixture.ChangeReceipt(valid, "wrong schema type", item => item[Native5ServerProofFixture.SchemaVersion] = "1"),
            Native5ServerProofMutationFixture.ChangeReceiptSection(valid, "wrong producer field type", Native5ServerProofFixture.Producer, item => item[Native5ServerProofFixture.RunId] = 123),
            Native5ServerProofMutationFixture.WithRawReceipt(valid, "duplicate receipt property", duplicateReceipt),
            Native5ServerProofMutationFixture.WithRawReceipt(valid, "escaped duplicate receipt property", escapedReceipt),
            Native5ServerProofMutationFixture.WithRawManifestBound(valid, "duplicate manifest property", duplicateManifest),
            Native5ServerProofMutationFixture.WithRawManifestBound(valid, "escaped duplicate manifest property", escapedManifest),
            Native5ServerProofMutationFixture.WithRawManifestBound(valid, "escaped duplicate descriptor key", escapedDescriptor),
            Native5ServerProofMutationFixture.WithRawManifestBound(valid, "duplicate layer descriptor key", duplicateLayerDigest),
            Native5ServerProofMutationFixture.WithRawManifestBound(valid, "duplicate annotation key", duplicateAnnotation),
            Native5ServerProofMutationFixture.WithRawManifestBound(valid, "escaped duplicate annotation key", escapedAnnotation),
            Native5ServerProofMutationFixture.WithRawManifestBound(descriptorAnnotations, "duplicate descriptor annotation key", duplicateDescriptorAnnotation),
            Native5ServerProofMutationFixture.WithRawReceipt(valid, "receipt exceeds byte cap", valid.Receipt + new string(' ', 65_537)),
            Native5ServerProofMutationFixture.OversizedManifest(valid),
            Native5ServerProofMutationFixture.WithRawManifest(valid, "manifest excessive nesting", "{\"schemaVersion\":2,\"mediaType\":\"application/vnd.oci.image.manifest.v1+json\",\"config\":{\"mediaType\":\"application/vnd.oci.image.config.v1+json\",\"digest\":\"sha256:1111111111111111111111111111111111111111111111111111111111111111\",\"size\":1},\"layers\":[" + new string('[', 66) + new string(']', 66) + "]}")
        ];
    }

    private static Native5ServerParserCase ReceiptProducer(Native5ServerParserCase source, string property,
        string name)
        => ReceiptProducer(source, property, new string('c', 40), name);

    private static Native5ServerParserCase ReceiptProducer(Native5ServerParserCase source, string property,
        string value, string name)
        => Native5ServerProofMutationFixture.ChangeReceiptSection(source, name, Native5ServerProofFixture.Producer,
            item => item[property] = value);

    private static Native5ServerParserCase ReceiptSource(Native5ServerParserCase source, string property,
        object value, string name)
        => Native5ServerProofMutationFixture.ChangeReceiptSection(source, name, Native5ServerProofFixture.ImageSource,
            item => item[property] = JsonSerializerNode(value));

    private static Native5ServerParserCase ReceiptBase(Native5ServerParserCase source, string property,
        string value, string name)
        => Native5ServerProofMutationFixture.ChangeReceiptSection(source, name, Native5ServerProofFixture.Bases,
            item => item[property] = value);

    private static Native5ServerParserCase ReceiptImage(Native5ServerParserCase source, string property,
        string value, string name)
        => Native5ServerProofMutationFixture.ChangeReceiptSection(source, name, Native5ServerProofFixture.Image,
            item => item[property] = value);

    private static JsonNode? JsonSerializerNode(object value)
        => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(value));
}
