using System.Windows;

namespace CaveAiProForWindows.Views;

public partial class LinkedLibraryCaveIdPromptWindow : Window
{
    public LinkedLibraryCaveIdPromptWindow()
    {
        InitializeComponent();
    }

    public string? EnteredDocId { get; private set; }

    public static bool TryPrompt(Window? owner, string? initialValue, out string? docId)
    {
        var dlg = new LinkedLibraryCaveIdPromptWindow
        {
            Owner = owner,
            EnteredDocId = null,
        };
        if (!string.IsNullOrWhiteSpace(initialValue))
            dlg.DocIdBox.Text = initialValue.Trim();

        var ok = dlg.ShowDialog() == true;
        docId = ok ? dlg.EnteredDocId : null;
        return ok;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateAndAccept(DocIdBox.Text))
            return;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryCavePickerWindow.TryPick(this, out var picked) && !string.IsNullOrWhiteSpace(picked))
        {
            DocIdBox.Text = picked.Trim();
            if (TryValidateAndAccept(picked))
                DialogResult = true;
        }
    }

    private bool TryValidateAndAccept(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            MessageBox.Show(this, "Enter the Cave Library document id or browse the map.", "Required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DocIdBox.Focus();
            return false;
        }

        EnteredDocId = trimmed;
        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
