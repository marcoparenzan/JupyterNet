using System.Text.Json;
using System.Text.Json.Serialization;

namespace KerNet.Protocol;

/// <summary>
/// Reads/writes <see cref="HostRequest"/>/<see cref="HostEvent"/> as one JSON object per line
/// (NDJSON) — the whole wire format between <c>KerNet.Host</c> and the VS Code extension.
/// </summary>
public static class NdjsonProtocol
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static HostRequest? ParseRequest(string line) =>
        string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<HostRequest>(line, Options);

    public static void WriteEvent(TextWriter writer, HostEvent evt)
    {
        writer.WriteLine(JsonSerializer.Serialize(evt, Options));
        writer.Flush();
    }

    public static string SerializeRequest(HostRequest request) =>
        JsonSerializer.Serialize(request, Options);
}
