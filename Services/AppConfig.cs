using System.IO;
using System.Text.Json;

namespace AssinadorDigital.Services;

public class AppConfig
{
    // Caminhos padrao relativos a pasta onde o proprio .exe esta rodando -
    // assim funcionam tanto aqui no desenvolvimento quanto numa copia
    // levada pra outra maquina, desde que as pastas certificado/ e signtool/
    // viajem junto com o .exe.
    public string SignToolPath { get; set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "signtool", "signtool.exe");

    public string PfxPath { get; set; } =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "certificado", "CertificadoAutenticacao.pfx");

    public string TimestampUrl { get; set; } = "http://timestamp.digicert.com";

    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssinadorDigital");

    private static readonly string ConfigPath = Path.Combine(Folder, "config.json");

    public static AppConfig Carregar()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Salvar()
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }
}
