using System.Text.Json;
using System.Text.Json.Serialization;
using Legendary_Sharp.Fortnite;

namespace Legendary_Sharp.Application;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(InstallRecord))]
[JsonSerializable(typeof(QueueTicket))]
[JsonSerializable(typeof(SavedQueue))]
[JsonSerializable(typeof(ProbeCacheFile))]
internal sealed partial class AppJson : JsonSerializerContext;
