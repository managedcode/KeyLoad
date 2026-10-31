using System.Globalization;

namespace KeyLoad.ComparisonHost.Queues;

/// <summary>A message of the standard queue dataset.</summary>
internal sealed record QueueMessage(string Id, string Body);

/// <summary>A received message and the handle its acknowledgement needs (delivery tag, lease token).</summary>
internal sealed record QueueDelivery(QueueMessage Message, object Handle);

/// <summary>The standard queue dataset: <c>m#########</c> messages with 1 KiB ASCII bodies.</summary>
internal static class QueueDataset
{
    internal const int BodyBytes = 1024;
    private const string IdPrefix = "m";
    private const string Format = "D9";
    private const char Padding = 'x';

    internal static string Id(int number) => IdPrefix + number.ToString(Format, CultureInfo.InvariantCulture);

    internal static int Number(string id) => int.Parse(id.AsSpan(IdPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture);

    internal static QueueMessage Create(int number)
    {
        var id = Id(number);
        return new(id, id + new string(Padding, BodyBytes - id.Length));
    }
}
