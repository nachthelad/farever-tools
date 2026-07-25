using System.Windows;
using FareverTranslate.Core.Security;

namespace FareverTranslate.Host;

public partial class ApiKeyWindow : Window
{
    private readonly SecretStore secretStore;

    internal ApiKeyWindow(SecretStore secretStore)
    {
        InitializeComponent();
        this.secretStore = secretStore;
        CurrentState.Text = secretStore.TryLoadApiKey(out _)
            ? "Hay una clave configurada."
            : "Todavía no hay una clave configurada.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(KeyBox.Password))
        {
            CurrentState.Text = "Pegá una clave antes de guardar.";
            return;
        }
        secretStore.SaveApiKey(KeyBox.Password);
        DialogResult = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        secretStore.DeleteApiKey();
        KeyBox.Clear();
        CurrentState.Text = "La clave fue eliminada.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
