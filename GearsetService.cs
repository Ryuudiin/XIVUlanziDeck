using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Data.Files;
using Lumina.Excel.Sheets;

namespace XivUlanzi;

/// <summary>Lê os gearsets do jogo e extrai os ícones de job como PNG.</summary>
public sealed unsafe class GearsetService
{
    public const int MaxGearsets = 100;

    // O ícone do job tem id 62100 + id do ClassJob (ex.: 62119 = Paladino).
    private const uint JobIconBase = 62100;

    private readonly IDataManager data;
    private readonly ITextureProvider textures;

    public GearsetService(IDataManager data, ITextureProvider textures)
    {
        this.data = data;
        this.textures = textures;
    }

    public sealed record GearsetInfo(int Number, string Name, int ClassJob, string Abbreviation);

    /// <summary>Chamar somente na thread principal do jogo.</summary>
    public List<GearsetInfo> List()
    {
        var module = RaptureGearsetModule.Instance();
        if (module == null) throw new InvalidOperationException("Módulo de gearsets indisponível.");

        var jobs = data.GetExcelSheet<ClassJob>();
        var result = new List<GearsetInfo>();

        for (var i = 0; i < MaxGearsets; i++)
        {
            if (!module->IsValidGearset(i)) continue;

            var entry = module->GetGearset(i);
            var jobId = entry->ClassJob;
            var abbr = jobs.TryGetRow(jobId, out var row) ? row.Abbreviation.ToString() : "";

            // Número exibido no jogo = índice + 1 (é o que o "/gearset change" espera).
            result.Add(new GearsetInfo(i + 1, entry->NameString, jobId, abbr));
        }
        return result;
    }

    /// <summary>Número (1-100) do gearset equipado, ou 0 se nenhum. Somente na thread principal.</summary>
    public int Current()
    {
        var module = RaptureGearsetModule.Instance();
        return module == null ? 0 : module->CurrentGearsetIndex + 1;
    }

    /// <summary>PNG (base64) do ícone do job, lido dos arquivos do próprio jogo. Pode rodar em qualquer thread.</summary>
    public string GetJobIconBase64(int classJob)
    {
        if (classJob <= 0 || classJob > 100) throw new ArgumentException("ClassJob inválido.");

        var lookup = new GameIconLookup(JobIconBase + (uint)classJob);
        if (!textures.TryGetIconPath(lookup, out var path) || path is null)
            throw new FileNotFoundException($"Ícone do job {classJob} não encontrado.");

        var tex = data.GetFile<TexFile>(path) ?? throw new FileNotFoundException($"Não foi possível ler {path}");

        // ImageData vem em BGRA; o PNG precisa de RGBA.
        var bgra = tex.ImageData;
        var rgba = new byte[bgra.Length];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        return Convert.ToBase64String(PngEncoder.Encode(tex.Header.Width, tex.Header.Height, rgba));
    }
}
