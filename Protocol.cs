using System.Text.Json;
using System.Text.Json.Serialization;

namespace XivUlanzi.Server;

/// <summary>Mensagem recebida do cliente (plugin do Ulanzi).</summary>
public sealed class Request
{
    [JsonPropertyName("id")]     public string? Id { get; set; }
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("data")]   public JsonElement? Data { get; set; }
}

/// <summary>Resposta a uma Request (mesmo id).</summary>
public sealed class Response
{
    [JsonPropertyName("id")]    public string? Id { get; set; }
    [JsonPropertyName("ok")]    public bool Ok { get; set; }
    [JsonPropertyName("data")]  public object? Data { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

/// <summary>Evento enviado pelo servidor sem requisição (ex.: troca de gearset).</summary>
public sealed class ServerEvent
{
    [JsonPropertyName("event")] public string Event { get; set; } = "";
    [JsonPropertyName("data")]  public object? Data { get; set; }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
