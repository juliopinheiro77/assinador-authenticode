using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AssinadorDigital.Models;
using AssinadorDigital.Services;
using Microsoft.Win32;

namespace AssinadorDigital;

public class LogEntry
{
    public string Texto { get; init; } = "";
    public Brush Cor { get; init; } = Brushes.White;
}

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SignableFile> _arquivos = new();
    private readonly ObservableCollection<LogEntry> _log = new();
    private AppConfig _config = AppConfig.Carregar();
    private bool _assinando;
    private bool _configEhPrimeiroUso;
    private bool _senhaAlterada;
    private bool _suprimirEventoSenha;

    private const string SenhaPlaceholder = "••••••••";

    private static readonly Brush CorInfo = new SolidColorBrush(Color.FromRgb(0x7A, 0xA2, 0xE3));
    private static readonly Brush CorSucesso = new SolidColorBrush(Color.FromRgb(0x6F, 0xD9, 0x8F));
    private static readonly Brush CorErro = new SolidColorBrush(Color.FromRgb(0xE0, 0x6B, 0x6B));
    private static readonly Brush CorAviso = new SolidColorBrush(Color.FromRgb(0xC7, 0x8A, 0x1E));
    private static readonly Brush CorMuted = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x99));

    public MainWindow()
    {
        InitializeComponent();
        ArquivosList.ItemsSource = _arquivos;
        LogList.ItemsSource = _log;

        _arquivos.CollectionChanged += (_, _) => AtualizaEstadoBotoes();

        Loaded += (_, _) =>
        {
            if (!CredentialStore.TemSenhaConfigurada(_config.PfxPath))
                AbrirConfig(primeiroUso: true);

            AtualizarInfoCertificado();
        };
    }

    // ---------- Certificado ----------

    private void AtualizarInfoCertificado()
    {
        var senha = CredentialStore.LerSenha(_config.PfxPath);

        if (senha is null || !File.Exists(_config.PfxPath))
        {
            CertificadoNomeText.Text = "Certificado: não configurado";
            CertificadoValidadeText.Text = "";
            return;
        }

        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(_config.PfxPath, senha);

            var nome = cert.GetNameInfo(X509NameType.SimpleName, false);
            CertificadoNomeText.Text = string.IsNullOrWhiteSpace(nome) ? "Certificado: API Systems" : $"Certificado: {nome}";

            int diasRestantes = (cert.NotAfter.Date - DateTime.Today).Days;

            if (diasRestantes < 0)
            {
                CertificadoValidadeText.Text = $"Certificado VENCIDO em {cert.NotAfter:dd/MM/yyyy}";
                CertificadoValidadeText.Foreground = CorErro;
            }
            else if (diasRestantes <= 30)
            {
                CertificadoValidadeText.Text = $"Válido até {cert.NotAfter:dd/MM/yyyy} ({diasRestantes} dias)";
                CertificadoValidadeText.Foreground = CorAviso;
            }
            else
            {
                CertificadoValidadeText.Text = $"Válido até {cert.NotAfter:dd/MM/yyyy}";
                CertificadoValidadeText.Foreground = CorMuted;
            }
        }
        catch
        {
            CertificadoNomeText.Text = "Certificado: API Systems";
            CertificadoValidadeText.Text = "Não foi possível ler o certificado";
            CertificadoValidadeText.Foreground = CorErro;
        }
    }

    // ---------- Titlebar ----------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Config_Click(object sender, RoutedEventArgs e) => AbrirConfig(primeiroUso: false);

    // ---------- Arquivos ----------

    private void Procurar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione os arquivos para assinar",
            Filter = "Executáveis e bibliotecas (*.exe;*.dll)|*.exe;*.dll|Todos os arquivos (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
            AdicionarArquivos(dialog.FileNames);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var caminhos = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            AdicionarArquivos(caminhos);
        }
    }

    private void AdicionarArquivos(IEnumerable<string> caminhos)
    {
        foreach (var caminho in caminhos)
        {
            if (!File.Exists(caminho))
                continue;

            if (_arquivos.Any(a => string.Equals(a.FullPath, caminho, StringComparison.OrdinalIgnoreCase)))
                continue;

            _arquivos.Add(new SignableFile(caminho));
        }

        SemArquivosText.Visibility = _arquivos.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AtualizaEstadoBotoes()
    {
        SemArquivosText.Visibility = _arquivos.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        AssinarBtn.IsEnabled = _arquivos.Count > 0 && !_assinando;
    }

    // ---------- Assinatura ----------

    private async void Assinar_Click(object sender, RoutedEventArgs e)
    {
        var senha = CredentialStore.LerSenha(_config.PfxPath);
        if (senha is null)
        {
            AdicionaLog("Senha do certificado nao configurada nesta maquina.", CorErro);
            AbrirConfig(primeiroUso: true);
            return;
        }

        _assinando = true;
        AtualizaEstadoBotoes();
        StatusRodape.Text = "Assinando...";

        int sucesso = 0, falha = 0;

        foreach (var arquivo in _arquivos.Where(a => a.Status != SignStatus.Assinado))
        {
            arquivo.Status = SignStatus.Assinando;
            AdicionaLog($"Assinando {arquivo.FileName}...", CorInfo);

            var resultado = await SignerService.AssinarAsync(_config, senha, arquivo.FullPath);

            if (resultado.Sucesso)
            {
                arquivo.Status = SignStatus.Assinado;
                AdicionaLog($"{arquivo.FileName} assinado com sucesso.", CorSucesso);
                sucesso++;
            }
            else
            {
                arquivo.Status = SignStatus.Erro;
                AdicionaLog($"Falha ao assinar {arquivo.FileName}: {resultado.Saida}", CorErro);
                falha++;
            }
        }

        StatusRodape.Text = falha == 0
            ? $"{sucesso} arquivo(s) assinado(s) com sucesso."
            : $"{sucesso} assinado(s), {falha} com falha.";

        _assinando = false;
        AtualizaEstadoBotoes();
    }

    private void AdicionaLog(string texto, Brush cor)
    {
        var hora = DateTime.Now.ToString("HH:mm:ss");
        _log.Add(new LogEntry { Texto = $"{hora}  {texto}", Cor = cor });
        LogScroll.ScrollToEnd();
    }

    // ---------- Procurar arquivos na configuracao ----------

    private void ProcurarSignTool_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Localizar signtool.exe",
            Filter = "signtool.exe|signtool.exe|Executáveis (*.exe)|*.exe|Todos os arquivos (*.*)|*.*"
        };

        if (File.Exists(SignToolPathBox.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(SignToolPathBox.Text);

        if (dialog.ShowDialog() == true)
            SignToolPathBox.Text = dialog.FileName;
    }

    private void ProcurarPfx_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Localizar certificado (.pfx)",
            Filter = "Certificado PFX (*.pfx)|*.pfx|Todos os arquivos (*.*)|*.*"
        };

        if (File.Exists(PfxPathBox.Text))
            dialog.InitialDirectory = Path.GetDirectoryName(PfxPathBox.Text);

        if (dialog.ShowDialog() == true)
            PfxPathBox.Text = dialog.FileName;
    }

    // ---------- Configuracao ----------

    private void AbrirConfig(bool primeiroUso)
    {
        _configEhPrimeiroUso = primeiroUso;
        _config = AppConfig.Carregar();

        SignToolPathBox.Text = _config.SignToolPath;
        PfxPathBox.Text = _config.PfxPath;
        TimestampUrlBox.Text = _config.TimestampUrl;
        ConfigErro.Visibility = Visibility.Collapsed;

        _suprimirEventoSenha = true;
        bool temSenha = CredentialStore.TemSenhaConfigurada(_config.PfxPath);
        SenhaBox.Password = temSenha ? SenhaPlaceholder : "";
        _suprimirEventoSenha = false;
        _senhaAlterada = false;

        SenhaDica.Text = temSenha
            ? "Senha salva nesta máquina. Digite uma nova senha só se quiser trocá-la."
            : "Nenhuma senha salva ainda nesta máquina.";

        ConfigSubtitulo.Text = primeiroUso
            ? "Primeira vez nesta máquina - configure o certificado e a senha."
            : "Altere os caminhos ou a senha do certificado usados nesta máquina.";

        CancelarConfigBtn.Visibility = primeiroUso ? Visibility.Collapsed : Visibility.Visible;

        ConfigOverlay.Visibility = Visibility.Visible;
    }

    private void SenhaBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suprimirEventoSenha)
            return;

        _senhaAlterada = true;
        SenhaDica.Text = "Senha será atualizada ao salvar.";
    }

    private void CancelarConfig_Click(object sender, RoutedEventArgs e)
    {
        ConfigOverlay.Visibility = Visibility.Collapsed;
    }

    private void SalvarConfig_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SignToolPathBox.Text) || string.IsNullOrWhiteSpace(PfxPathBox.Text))
        {
            MostrarErroConfig("Preencha o caminho do signtool.exe e do certificado.");
            return;
        }

        if (_configEhPrimeiroUso && string.IsNullOrWhiteSpace(SenhaBox.Password))
        {
            MostrarErroConfig("Digite a senha do certificado.");
            return;
        }

        _config.SignToolPath = SignToolPathBox.Text.Trim();
        _config.PfxPath = PfxPathBox.Text.Trim();
        _config.TimestampUrl = string.IsNullOrWhiteSpace(TimestampUrlBox.Text)
            ? new AppConfig().TimestampUrl
            : TimestampUrlBox.Text.Trim();
        _config.Salvar();

        if (_senhaAlterada && !string.IsNullOrEmpty(SenhaBox.Password))
            CredentialStore.SalvarSenha(_config.PfxPath, SenhaBox.Password);

        ConfigOverlay.Visibility = Visibility.Collapsed;
        AdicionaLog("Configuração salva.", CorInfo);
        AtualizarInfoCertificado();
        VerificarEOferecerConfianca();
    }

    private void MostrarErroConfig(string mensagem)
    {
        ConfigErro.Text = mensagem;
        ConfigErro.Visibility = Visibility.Visible;
    }

    // ---------- Criar certificado novo ----------

    private bool _sufixoEditadoManualmente;
    private bool _suprimirEventoSufixo;

    private void AbrirCriarCertificado_Click(object sender, RoutedEventArgs e)
    {
        _sufixoEditadoManualmente = false;
        NovoCertNomeBox.Text = "API Systems";
        NovoCertAnosBox.Text = "5";
        NovoCertSenhaBox.Password = "";
        NovoCertSenhaConfirmaBox.Password = "";
        NovoCertErro.Visibility = Visibility.Collapsed;

        AtualizarSufixoAutomatico();
        AtualizarPreviaDestino();

        CriarCertOverlay.Visibility = Visibility.Visible;
    }

    private void NovoCertNomeBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (NovoCertSufixoBox is null)
            return;

        if (!_sufixoEditadoManualmente)
            AtualizarSufixoAutomatico();

        AtualizarPreviaDestino();
    }

    private void NovoCertSufixoBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (NovoCertDestino is null)
            return;

        if (!_suprimirEventoSufixo)
            _sufixoEditadoManualmente = true;

        AtualizarPreviaDestino();
    }

    private void AtualizarSufixoAutomatico()
    {
        _suprimirEventoSufixo = true;
        NovoCertSufixoBox.Text = SanitizarNomeArquivo(NovoCertNomeBox.Text);
        _suprimirEventoSufixo = false;
    }

    private static string SanitizarNomeArquivo(string texto)
    {
        var semEspacos = texto.Replace(" ", "");
        var valido = new string(semEspacos.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return valido;
    }

    private string MontarCaminhoCertificadoNovo()
    {
        var pastaBase = string.IsNullOrWhiteSpace(PfxPathBox.Text)
            ? Path.GetDirectoryName(new AppConfig().PfxPath)
            : Path.GetDirectoryName(PfxPathBox.Text.Trim());

        if (string.IsNullOrWhiteSpace(pastaBase))
            pastaBase = AppDomain.CurrentDomain.BaseDirectory;

        var sufixo = SanitizarNomeArquivo(NovoCertSufixoBox.Text);
        var nomeArquivo = string.IsNullOrWhiteSpace(sufixo)
            ? "CertificadoAutenticacao.pfx"
            : $"CertificadoAutenticacao_{sufixo}.pfx";

        return Path.Combine(pastaBase, nomeArquivo);
    }

    private void AtualizarPreviaDestino()
    {
        NovoCertDestino.Text = $"Será salvo em: {MontarCaminhoCertificadoNovo()}";
    }

    private void CancelarCriarCertificado_Click(object sender, RoutedEventArgs e)
    {
        CriarCertOverlay.Visibility = Visibility.Collapsed;
    }

    private void CriarCertificado_Click(object sender, RoutedEventArgs e)
    {
        var nome = NovoCertNomeBox.Text.Trim();
        var senha = NovoCertSenhaBox.Password;
        var confirmacao = NovoCertSenhaConfirmaBox.Password;

        if (string.IsNullOrWhiteSpace(nome))
        {
            MostrarErroCriarCert("Digite um nome para o certificado.");
            return;
        }

        if (!int.TryParse(NovoCertAnosBox.Text, out int anos) || anos <= 0)
        {
            MostrarErroCriarCert("Digite um número válido de anos de validade.");
            return;
        }

        if (string.IsNullOrEmpty(senha))
        {
            MostrarErroCriarCert("Digite uma senha para proteger o certificado.");
            return;
        }

        if (senha != confirmacao)
        {
            MostrarErroCriarCert("As senhas digitadas não conferem.");
            return;
        }

        var destino = MontarCaminhoCertificadoNovo();

        if (File.Exists(destino))
        {
            var resposta = MessageBox.Show(
                $"Já existe um certificado em:\n{destino}\n\nDeseja substituí-lo? Isso não pode ser desfeito.",
                "Substituir certificado existente?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (resposta != MessageBoxResult.Yes)
                return;
        }

        try
        {
            CertificateGenerator.CriarCertificadoAutoAssinado(destino, senha, nome, anos);

            PfxPathBox.Text = destino;

            CredentialStore.SalvarSenha(destino, senha);
            _suprimirEventoSenha = true;
            SenhaBox.Password = SenhaPlaceholder;
            _suprimirEventoSenha = false;
            _senhaAlterada = false;
            SenhaDica.Text = "Senha salva nesta máquina. Digite uma nova senha só se quiser trocá-la.";

            CriarCertOverlay.Visibility = Visibility.Collapsed;
            AdicionaLog($"Certificado novo criado: {nome}, válido por {anos} ano(s).", CorSucesso);
            AtualizarInfoCertificado();
        }
        catch (Exception ex)
        {
            MostrarErroCriarCert($"Falha ao criar o certificado: {ex.Message}");
        }
    }

    private void MostrarErroCriarCert(string mensagem)
    {
        NovoCertErro.Text = mensagem;
        NovoCertErro.Visibility = Visibility.Visible;
    }

    // ---------- Confiar no certificado (Autoridades Confiaveis) ----------

    private void ConfiarCertificado_Click(object sender, RoutedEventArgs e)
    {
        // Usa exatamente o que esta digitado na tela agora (salvo ou nao) - mas deixa
        // bem explicito na confirmacao qual certificado/caminho sera afetado, pra nunca
        // confiar em algo sem o usuario ver claramente o que esta sendo confirmado.
        var pfxPath = string.IsNullOrWhiteSpace(PfxPathBox.Text) ? _config.PfxPath : PfxPathBox.Text.Trim();

        if (!File.Exists(pfxPath))
        {
            MostrarErroConfig("Configure e salve o caminho do certificado antes de confiar nele.");
            return;
        }

        var senha = _senhaAlterada ? SenhaBox.Password : CredentialStore.LerSenha(pfxPath);
        if (string.IsNullOrEmpty(senha))
        {
            MostrarErroConfig("Não foi possível ler a senha do certificado. Configure a senha primeiro.");
            return;
        }

        X509Certificate2 cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, senha);
        }
        catch (Exception ex)
        {
            AdicionaLog($"Falha ao confiar no certificado: {ex.Message}", CorErro);
            return;
        }

        if (CertificadoEstaConfiavel(cert))
        {
            AdicionaLog($"\"{cert.GetNameInfo(X509NameType.SimpleName, false)}\" já está nas Autoridades Confiáveis desta máquina. Nada a fazer.", CorInfo);
            cert.Dispose();
            return;
        }

        var nomeCert = cert.GetNameInfo(X509NameType.SimpleName, false);
        MostrarConfirmacaoConfianca(
            "Confiar no certificado?",
            $"Certificado: {nomeCert}\n" +
            $"Arquivo: {pfxPath}\n\n" +
            "Iremos adicionar este certificado à lista de Autoridades Confiáveis desta máquina (usuário atual).\n\n" +
            "Esse passo é importante para que o Windows reconheça o certificado como válido e pare de mostrar o aviso de \"editor desconhecido\" nesta máquina.",
            aoConfirmar: () => { ImportarNaListaDeConfiaveis(cert); cert.Dispose(); },
            aoCancelar: () => cert.Dispose());
    }

    private static bool CertificadoEstaConfiavel(X509Certificate2 cert)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint!, validOnly: false).Count > 0;
    }

    private void ImportarNaListaDeConfiaveis(X509Certificate2 cert)
    {
        AdicionarCertEmLoja(cert, StoreName.Root);
        AdicionarCertEmLoja(cert, StoreName.TrustedPublisher);

        AdicionaLog($"Certificado \"{cert.GetNameInfo(X509NameType.SimpleName, false)}\" adicionado às Autoridades Confiáveis desta máquina.", CorSucesso);

        // Confere se a cadeia de confianca agora fecha sem erro.
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        bool confiavel = chain.Build(cert);

        AdicionaLog(confiavel
            ? "Cadeia de confiança verificada: o Windows agora reconhece este certificado nesta máquina."
            : "O certificado foi adicionado, mas a cadeia de confiança ainda não fechou - pode ser necessário reiniciar o aplicativo.",
            confiavel ? CorSucesso : CorAviso);
    }

    private static void AdicionarCertEmLoja(X509Certificate2 cert, StoreName nomeLoja)
    {
        using var store = new X509Store(nomeLoja, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
        store.Close();
    }

    /// <summary>
    /// Chamado logo apos salvar a configuracao: se o certificado configurado ainda
    /// nao estiver nas Autoridades Confiaveis desta maquina, pergunta se o usuario
    /// quer adicionar agora, explicando o que acontece em cada caso.
    /// </summary>
    private void VerificarEOferecerConfianca()
    {
        if (!File.Exists(_config.PfxPath))
            return;

        var senha = CredentialStore.LerSenha(_config.PfxPath);
        if (string.IsNullOrEmpty(senha))
            return;

        X509Certificate2? cert;
        try
        {
            cert = X509CertificateLoader.LoadPkcs12FromFile(_config.PfxPath, senha);
        }
        catch
        {
            return;
        }

        if (CertificadoEstaConfiavel(cert))
        {
            cert.Dispose();
            return;
        }

        var nome = cert.GetNameInfo(X509NameType.SimpleName, false);
        MostrarConfirmacaoConfianca(
            "Certificado ainda não confiável nesta máquina",
            $"O certificado \"{nome}\" ainda não consta na lista de Autoridades Confiáveis do Windows nesta máquina.\n\n" +
            "Se adicionar: o Windows passa a reconhecer os arquivos assinados com ele como confiáveis nesta máquina, sem o aviso de \"editor desconhecido\".\n\n" +
            "Se não adicionar: a assinatura continua funcionando normalmente, mas o Windows pode mostrar o aviso de \"editor desconhecido\" ao abrir arquivos assinados com ele nesta máquina.",
            aoConfirmar: () => { ImportarNaListaDeConfiaveis(cert); cert.Dispose(); },
            aoCancelar: () => cert.Dispose());
    }

    // ---------- Painel proprio de confirmacao (nunca usa MessageBox nativo, pra nao confundir com alerta do Windows) ----------

    private Action? _aoConfirmarConfianca;
    private Action? _aoCancelarConfianca;

    private void MostrarConfirmacaoConfianca(string titulo, string mensagem, Action aoConfirmar, Action? aoCancelar = null)
    {
        ConfiarTitulo.Text = titulo;
        ConfiarMensagem.Text = mensagem;
        _aoConfirmarConfianca = aoConfirmar;
        _aoCancelarConfianca = aoCancelar;
        ConfiarOverlay.Visibility = Visibility.Visible;
    }

    private void ConfiarSim_Click(object sender, RoutedEventArgs e)
    {
        ConfiarOverlay.Visibility = Visibility.Collapsed;
        var acao = _aoConfirmarConfianca;
        _aoConfirmarConfianca = null;
        _aoCancelarConfianca = null;
        acao?.Invoke();
    }

    private void ConfiarNao_Click(object sender, RoutedEventArgs e)
    {
        ConfiarOverlay.Visibility = Visibility.Collapsed;
        var acao = _aoCancelarConfianca;
        _aoConfirmarConfianca = null;
        _aoCancelarConfianca = null;
        acao?.Invoke();
    }
}
