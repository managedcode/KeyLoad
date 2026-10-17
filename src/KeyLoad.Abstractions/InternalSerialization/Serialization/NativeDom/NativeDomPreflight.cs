using System.Text;
using System.Text.Json;

namespace KeyLoad.Features.InternalSerialization;

// A native structural surrogate must be a true tree. This walk charges escaped output before
// allocating the JSON output, including repeated strings shared between otherwise distinct nodes.
internal sealed class NativeDomPreflight
{
    private const int RootDepth = 0;
    private const int EmptyItemCount = 0;
    private const int UnseparatedFirstItem = 1;
    private const int ContainerBytes = 2;
    private const int SeparatorBytes = 1;
    private const int StringFramingBytes = 2;
    private const int PropertyFramingBytes = 3;
    private const int TrueBytes = 4;
    private const int FalseBytes = 5;
    private const int NullBytes = 4;
    private readonly HashSet<JsonTreeNode> nodes = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> numbers = new(ReferenceEqualityComparer.Instance);
    private readonly NativeDomStringSizes strings = new();
    private long bytes;
    private int work;

    internal static void Validate(JsonTreeNode? root) => new NativeDomPreflight().Visit(root, RootDepth);

    private void Visit(JsonTreeNode? node, int depth)
    {
        ChargeWork();
        if (node is null || !nodes.Add(node))
        {
            Fail();
        }
        NativeDomShape.Validate(node!);
        switch (node!.Kind)
        {
            case JsonValueKind.Object:
                VisitObject(node.Properties!, NextDepth(depth));
                break;
            case JsonValueKind.Array:
                VisitArray(node.Items!, NextDepth(depth));
                break;
            case JsonValueKind.String:
                Charge(StringFramingBytes + strings.Escaped(node.Text!));
                break;
            case JsonValueKind.Number:
                Charge(Encoding.UTF8.GetByteCount(node.Text!));
                ValidateNumber(node.Text!);
                break;
            case JsonValueKind.True:
                Charge(TrueBytes);
                break;
            case JsonValueKind.False:
                Charge(FalseBytes);
                break;
            case JsonValueKind.Null:
                Charge(NullBytes);
                break;
        }
    }

    private void VisitArray(JsonTreeNode[] items, int depth)
    {
        Charge(ContainerBytes + (long)Math.Max(EmptyItemCount, items.Length - UnseparatedFirstItem) * SeparatorBytes);
        foreach (var item in items)
        {
            Visit(item, depth);
        }
    }

    private void VisitObject(JsonTreeProperty[] properties, int depth)
    {
        Charge(ContainerBytes + (long)Math.Max(EmptyItemCount, properties.Length - UnseparatedFirstItem) * SeparatorBytes);
        foreach (var property in properties)
        {
            ChargeWork();
            if (property is null || property.Name is null || property.Value is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
            }
            Charge(PropertyFramingBytes + strings.Escaped(property.Name));
            Visit(property.Value, depth);
        }
    }

    private void ValidateNumber(string text)
    {
        if (numbers.Add(text))
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Number)
            {
                Fail();
            }
        }
    }

    private static int NextDepth(int depth)
    {
        if (++depth > NativeSerializationLimits.SemanticDepth)
        {
            Fail();
        }
        return depth;
    }

    private void Charge(long count)
    {
        bytes = checked(bytes + count);
        if (bytes > NativeSerializationLimits.MaximumDomBytes)
        {
            Fail();
        }
    }

    private void ChargeWork()
    {
        if (++work > NativeSerializationLimits.MaximumDomBytes)
        {
            Fail();
        }
    }

    private static void Fail() => throw Errors.Fail(ErrorCode.Corruption, NativePayloadVersion.InvalidPayload);
}
