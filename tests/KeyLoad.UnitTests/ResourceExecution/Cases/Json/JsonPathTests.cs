using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class JsonPathTests
{
    private const string EscapedPointer = "/a~1b/~0/~01/";
    private const string InvalidEscape = "/a~2";
    private const string TrailingEscape = "/a~";
    private const string MissingSlash = "field";
    private const string Source = "{\"a/b\":{\"~\":1}}";
    private const string Patched = "{\"a/b\":{\"~\":2}}";
    private const string Removed = "{\"a/b\":{}}";
    private const string Field = "/a~1b/~0";
    private const int MaximumPointerCharacters = 1_024;

    [Test]
    public async Task AcMp006PointerEncodingRetainsSlashTildeAndEmptySegments()
    {
        var expected = new[] { "a/b", "~", "~1", string.Empty };

        await Assert.That(JsonData.Path(expected)).IsEqualTo(EscapedPointer);
        await Assert.That(JsonData.PathSegments(EscapedPointer)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(JsonData.PathSegments(string.Empty)).IsEmpty();
        await Assert.That(JsonData.PathSegments("/" + new string('a', MaximumPointerCharacters - 1))).HasSingleItem();
    }

    [Test]
    public async Task AcMp006PointerValidationRejectsMalformedExcessiveAndNullInputs()
    {
        foreach (var pointer in new[] { InvalidEscape, TrailingEscape, MissingSlash,
                     "/" + new string('a', MaximumPointerCharacters) })
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => JsonData.PathSegments(pointer));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }
        await Assert.That(() => JsonData.PathSegments(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => JsonData.Path(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task AcMp006FieldPatchUsesTheSameEscapedPointerAndCanonicalNumberRules()
    {
        var changed = JsonData.Patch(Source, [new(Field, PatchKind.Set, "2.000")], new());
        var removed = JsonData.Patch(changed, [new(Field, PatchKind.Remove)], new());

        await Assert.That(changed).IsEqualTo(Patched);
        await Assert.That(removed).IsEqualTo(Removed);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Patch("{\"a\":[1]}",
            [new("/a/0", PatchKind.Remove)], new())).Code).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => JsonData.Patch(Source,
            [new("/*", PatchKind.Remove)], new())).Code).IsEqualTo(ErrorCode.Validation);
    }
}
