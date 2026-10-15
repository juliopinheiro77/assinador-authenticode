using System.ComponentModel;

namespace AssinadorDigital.Models;

public enum SignStatus
{
    Pendente,
    Assinando,
    Assinado,
    Erro
}

public class SignableFile : INotifyPropertyChanged
{
    public string FullPath { get; }
    public string FileName => System.IO.Path.GetFileName(FullPath);

    private SignStatus _status = SignStatus.Pendente;
    public SignStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status switch
    {
        SignStatus.Pendente => "Pendente",
        SignStatus.Assinando => "Assinando...",
        SignStatus.Assinado => "Assinado",
        SignStatus.Erro => "Erro",
        _ => ""
    };

    public SignableFile(string fullPath)
    {
        FullPath = fullPath;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
