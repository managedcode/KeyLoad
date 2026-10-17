using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class PreparedScalarTests
{
    private const string DocumentJson = "{\"a/b\":{\"~key\":[null,\"target\",12.5,true,{\"nested\":1}]}}";
    private const string StringPath = "/a~1b/~0key/1";
    private const string NullPath = "/a~1b/~0key/0";
    private const string NumberPath = "/a~1b/~0key/2";
    private const string BooleanPath = "/a~1b/~0key/3";
    private const string ObjectPath = "/a~1b/~0key/4";
    private const string MissingPath = "/a~1b/missing";
    private const string Target = "target";

    [Test]
    public async Task AcMp003PreparedPathsPreserveEscapingArraysAndScalarTypes()
    {
        using var json = JsonDocument.Parse(DocumentJson);
        var root = json.RootElement;

        await Assert.That(JsonData.Scalar(root, JsonData.PathSegments(StringPath))).IsEqualTo(Target);
        await Assert.That(JsonData.Scalar(root, JsonData.PathSegments(NullPath))).IsNull();
        await Assert.That(JsonData.Scalar(root, JsonData.PathSegments(NumberPath))).IsEqualTo(12.5m);
        await Assert.That((bool)JsonData.Scalar(root, JsonData.PathSegments(BooleanPath))!).IsTrue();
        await Assert.That(JsonData.Scalar(root, JsonData.PathSegments(MissingPath))).IsEqualTo(MissingValue.Instance);
    }

    [Test]
    public async Task AcMp003PreparedPathsKeepNonscalarRejectionAndOriginalPointerResults()
    {
        using var json = JsonDocument.Parse(DocumentJson);
        var root = json.RootElement;
        var paths = new[] { StringPath, NullPath, NumberPath, BooleanPath, MissingPath };
        foreach (var path in paths)
        {
            await Assert.That(JsonData.Scalar(root, JsonData.PathSegments(path))).IsEqualTo(JsonData.Scalar(root, path));
        }
        var error = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Scalar(root, JsonData.PathSegments(ObjectPath)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
    }
}
