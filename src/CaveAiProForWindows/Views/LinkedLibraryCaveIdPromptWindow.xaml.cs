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
        var text = DocIdBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "Enter the Cave Library document id.", "Required", MessageBoxButton.OK,
                MessageBoxImage.Information);
            DocIdBox.Focus();
            return;
        }

        EnteredDocId = text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
