using Dalamud.Configuration;

namespace XivUlanzi;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Porta do servidor WebSocket (apenas 127.0.0.1).</summary>
    public int Port { get; set; } = 37984;

    /// <summary>Token exigido na conexão (?token=...). Gerado automaticamente no primeiro uso.</summary>
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
}
