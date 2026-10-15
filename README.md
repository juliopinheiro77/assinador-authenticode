# 🔏 assinador-authenticode: 
#    Assinador Digital para Windows

Assina digitalmente executáveis, bibliotecas e instaladores do Windows (.exe, .dll, .msi, .ocx, .cab) com certificado A1, sem abrir o prompt de comando.

![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-0C54C2)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![Licença](https://img.shields.io/badge/licen%C3%A7a-MIT-718096)

![Demonstração](docs/demo.gif)

## Por que existe

Eu assinava cada build no prompt de comando, montando a linha do `signtool` com o caminho do certificado e a senha. Funcionava, mas era repetitivo e fácil de errar. Este app faz a mesma coisa com arrastar e soltar.

## Funcionalidades

- Assinatura Authenticode usando o `signtool` do Windows SDK
- Arrastar e soltar arquivos na janela
- Certificado A1 (`.pfx` / `.p12`): caminho e senha configurados uma vez
- Geração de certificado autoassinado pelo próprio app, com validade configurável em anos
- Instalação da cadeia de certificados no repositório de Autoridades Confiáveis do Windows em um clique
- Senha guardada com **DPAPI**, protegida pelo usuário do Windows (nunca em texto puro)

## Formatos suportados

| Formato | Situação |
|---------|----------|
| `.exe`, `.dll`, `.ocx` | ✅ Suportado |
| `.msi`, `.msp` | ✅ Suportado |
| `.cab` | ✅ Suportado |
| `.msix`, `.appx` | ⚠️ Assina, mas para instalar fora da Microsoft Store o pacote precisa de requisitos extras de identidade do app |
| `.sys` (drivers) | ❌ Não suportado oficialmente. O Windows 10/11 exige attestation signing com a Microsoft |
| `.ps1`, `.vbs`, `.js` | ❌ Não testado. Scripts usam um mecanismo de assinatura diferente |

O app não bloqueia nenhuma extensão. A tabela mostra o que foi testado e é suportado oficialmente.

## Requisitos 

- Windows 10 ou 11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) 
**Obs:** Há também a possibilidade de compilar o executável em release com o runtime do .NET 10 embutido (**self-contained**), neste caso, não será preciso instalar o .NET na máquina
- `signtool.exe` (vem com o Windows SDK)
- Certificado de assinatura de código A1 (`.pfx`) ou criar pelo próprio app 

## Como usar

1. Abra o app e selecione o arquivo `.pfx`.
2. Digite a senha do certificado (fica salva com DPAPI).
3. Arraste o executável para a janela e mande assinar.
4. Confira: botão direito no arquivo → Propriedades → Assinaturas Digitais.

### Dica - Estrutura de pastas recomendada

**Recomenda-se criar duas pastas na raiz do projeto:**
assinador-authenticode/
├── certificado/ # certificado A1 (.pfx / .p12) ou gerado pelo próprio app
└── signtool/ # signtool.exe (Windows SDK)

- **`certificado/`** — onde fica o certificado de assinatura. Pode ser um `.pfx` / `.p12` já existente ou um certificado autoassinado gerado pelo próprio app.
- **`signtool/`** — onde fica o `signtool.exe` (vem com o Windows SDK).

No app, basta apontar para essas duas pastas nas configurações. Os caminhos ficam salvos localmente. 

## Compilar

```bash
git clone https://github.com/juliopinheiro77/assinador-authenticode
cd assinador-authenticode
dotnet build -c Release
```

## Roadmap

- [ ] Certificado A3 (token USB ou cartão), assinando pelo certificado instalado no repositório do Windows
- [ ] Mais melhorias em análise...

## Segurança

Nenhum certificado, senha ou arquivo assinado de cliente faz parte deste repositório. O `.gitignore` bloqueia `*.pfx`, `*.p12` e arquivos de configuração local.

## Autor

Júlio Pinheiro · [portfólio](https://juliopinheiro77.github.io/portfolio) · [LinkedIn](https://linkedin.com/in/julio-pinheiro-5a521a137)
