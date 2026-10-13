using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cleaner.Core.Services;

public static class DiagnosticExport
{
    public static string CreateJson(object report, bool includePrivateDetails = false)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var node = JsonSerializer.SerializeToNode(report, options) ?? throw new InvalidDataException("Empty diagnostic report.");
        if (!includePrivateDetails) Redact(node);
        return node.ToJsonString(options);
    }
    private static void Redact(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                var name = pair.Key.ToLowerInvariant();
                if (name.Contains("path") || name.Contains("directory") || name is "storedname" or "filename" || name.Contains("error") && pair.Value is not JsonValue ||
                    name is "message" or "detail" or "issues" or "errortext" or "error")
                    obj[pair.Key] = pair.Value == null ? null : JsonValue.Create("[hidden]");
                else if (pair.Value != null) Redact(pair.Value);
            }
        }
        else if (node is JsonArray array) foreach (var item in array) if (item != null) Redact(item);
    }
}
