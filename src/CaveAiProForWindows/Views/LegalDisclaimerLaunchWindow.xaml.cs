using System.Windows;
using CaveAiProForWindows.Services.Legal;

namespace CaveAiProForWindows.Views;

/// <summary>
/// First-launch blocking disclaimer (Android DisclaimerScreen / web DisclaimerLaunchModal parity).
/// </summary>
public partial class LegalDisclaimerLaunchWindow : Window
{
    public LegalDisclaimerLaunchWindow()
    {
        InitializeComponent();
        DocumentMetaText.Text =
            $"Document version {LegalTexts.DocumentVersion} · Last updated {LegalTexts.LastUpdated} · {LegalTexts.PublisherName}";
        LegalRichTextFormatter.ApplyPlainText(DisclaimerRichText, LegalTexts.FullDisclaimerAndEula);
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void ReadConfirmCheckBox_Changed(object sender, RoutedEventArgs e) =>
        AcceptButton.IsEnabled = ReadConfirmCheckBox.IsChecked == true;

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
