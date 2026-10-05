using System.Text.Json.Serialization;
using KeyLoad.UnitTests.Features.ClusterRouting.Models;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Serialization;

[JsonSerializable(typeof(Interface3ServerSourceProbeResult))]
internal sealed partial class Interface3ServerSourceProbeJsonContext : JsonSerializerContext;
