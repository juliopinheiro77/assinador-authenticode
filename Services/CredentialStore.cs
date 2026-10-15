using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AssinadorDigital.Services;

/// <summary>
/// Guarda a senha de cada certificado criptografada com DPAPI, amarrada ao usuario/maquina atual.
/// Cada arquivo .pfx tem seu proprio "cofre" (nome derivado do caminho do arquivo), para que
/// testar ou criar um certificado novo nunca sobrescreva a senha de outro certificado ja usado.
/// </summary>
public static class CredentialStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssinadorDigital", "senhas");

    private static string CaminhoDoCofre(string pfxPath)
    {
        var chaveNormalizada = Path.GetFullPath(pfxPath).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chaveNormalizada)));
        return Path.Combine(Folder, $"{hash}.dat");
    }

    public static bool TemSenhaConfigurada(string pfxPath) => File.Exists(CaminhoDoCofre(pfxPath));

    public static void SalvarSenha(string pfxPath, string senha)
    {
        Directory.CreateDirectory(Folder);
        var bytes = Encoding.UTF8.GetBytes(senha);
        var protegido = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(CaminhoDoCofre(pfxPath), protegido);
    }

    public static string? LerSenha(string pfxPath)
    {
        var caminho = CaminhoDoCofre(pfxPath);
        if (!File.Exists(caminho))
            return null;

        try
        {
            var protegido = File.ReadAllBytes(caminho);
            var bytes = ProtectedData.Unprotect(protegido, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            // Arquivo veio de outra maquina/usuario - nao da pra descriptografar aqui.
            return null;
        }
    }

    public static void Limpar(string pfxPath)
    {
        var caminho = CaminhoDoCofre(pfxPath);
        if (File.Exists(caminho))
            File.Delete(caminho);
    }
}
