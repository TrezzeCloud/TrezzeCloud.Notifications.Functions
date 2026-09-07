using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrezzeCloud.Notifications.Functions.Serialization;

public static class MassTransitEnvelopeDeserializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static bool TryDeserialize<T>(string? payload, [NotNullWhen(true)] out T? message)
        where T : class
    {
        message = null;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("message", out var element) ||
                element.ValueKind != JsonValueKind.Object)
                return false;

            message = element.Deserialize<T>(Options);
            return message is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
