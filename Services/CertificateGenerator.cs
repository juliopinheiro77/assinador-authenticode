using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AssinadorDigital.Services;

/// <summary>
/// Gera um certificado autoassinado de assinatura de codigo (Code Signing),
/// equivalente ao que o script Cria_cert_auto_assinado.ps1 fazia - mas com
/// a validade realmente aplicada (o script antigo tinha um comentario
/// dizendo "5 anos" que nunca foi de fato configurado no New-SelfSignedCertificate).
/// </summary>
public static class CertificateGenerator
{
    private const string OidCodeSigning = "1.3.6.1.5.5.7.3.3";

    public static void CriarCertificadoAutoAssinado(string caminhoPfx, string senha, string nomeEmpresa, int anosValidade)
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            $"CN={nomeEmpresa}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid(OidCodeSigning) },
                critical: true));

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(anosValidade);

        using var cert = request.CreateSelfSigned(notBefore, notAfter);

        var pfxBytes = cert.Export(X509ContentType.Pfx, senha);

        var pasta = Path.GetDirectoryName(caminhoPfx);
        if (!string.IsNullOrEmpty(pasta))
            Directory.CreateDirectory(pasta);

        File.WriteAllBytes(caminhoPfx, pfxBytes);
    }
}
