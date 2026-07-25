using System.ComponentModel;
using System.Runtime.CompilerServices;
using FareverTranslate.Core.Models;

namespace FareverTranslate.Host;

public sealed class MessageRowViewModel : INotifyPropertyChanged
{
    private string? translation;
    private string status = string.Empty;
    private bool translating;

    internal MessageRowViewModel(ChatMessageSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    internal ChatMessageSnapshot Snapshot { get; }
    public string Identity => Snapshot.Identity;
    public string Sender => Snapshot.Sender;
    public string Channel => Snapshot.Channel;
    public string Original => Snapshot.Text;
    public string? Translation
    {
        get => translation;
        set
        {
            translation = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTranslation));
        }
    }
    public bool HasTranslation => !string.IsNullOrWhiteSpace(Translation);
    public string Status
    {
        get => status;
        set
        {
            status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool Translating
    {
        get => translating;
        set
        {
            translating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanTranslate));
            OnPropertyChanged(nameof(ButtonText));
        }
    }
    public bool CanTranslate => !Translating;
    public string ButtonText => Translating ? "…" : "T";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
