using System.Diagnostics;
using System.IO;

namespace AssinadorDigital.Services;

public record SignResult(bool Sucesso, string Saida);

public static class SignerService
{
    public static async Task<SignResult> AssinarAsync(AppConfig config, string senha, string arquivoAlvo)
    {
        if (!File.Exists(config.SignToolPath))
            return new SignResult(false, $"signtool.exe nao encontrado em: {config.SignToolPath}");

        if (!File.Exists(config.PfxPath))
            return new SignResult(false, $"Certificado .pfx nao encontrado em: {config.PfxPath}");

        var argumentos = $"sign /f \"{config.PfxPath}\" /p \"{senha}\" /fd sha256 /tr \"{config.TimestampUrl}\" /td sha256 \"{arquivoAlvo}\"";

        var psi = new ProcessStartInfo
        {
            FileName = config.SignToolPath,
            Arguments = argumentos,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var processo = new Process { StartInfo = psi };
        processo.Start();

        string saida = await processo.StandardOutput.ReadToEndAsync();
        string erro = await processo.StandardError.ReadToEndAsync();
        await processo.WaitForExitAsync();

        bool sucesso = processo.ExitCode == 0;
        string textoCompleto = string.IsNullOrWhiteSpace(erro) ? saida : $"{saida}\n{erro}";

        return new SignResult(sucesso, textoCompleto.Trim());
    }
}
