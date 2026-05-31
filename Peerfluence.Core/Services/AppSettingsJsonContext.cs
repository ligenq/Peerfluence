using Peerfluence.Core.Config;
using System.Text.Json.Serialization;

namespace Peerfluence.Core.Services;

[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(StorageSettings))]
[JsonSerializable(typeof(NetworkSettings))]
[JsonSerializable(typeof(ThemeSettings))]
[JsonSerializable(typeof(QueueSettings))]
[JsonSerializable(typeof(ProxySettings))]
[JsonSerializable(typeof(UpdateSettings))]
[JsonSerializable(typeof(CompletionActionSettings))]
[JsonSerializable(typeof(McpSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext
{
}
