using System.Windows;
using System.Windows.Input;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class CaveAiOfflineQaWindow : Window
{
    private readonly CaveProjectDocument _project;
    private readonly IReadOnlyList<KnownCaveRecord> _library;

    public CaveAiOfflineQaWindow(CaveProjectDocument project, IReadOnlyList<KnownCaveRecord> library)
    {
        _project = project;
        _library = library;
        InitializeComponent();
        AnswerText.Text = CaveAiOfflineBrain.Answer(_project, null, _library);
        QueryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Ask_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
    }

    private void Ask_Click(object sender, RoutedEventArgs e)
    {
        AnswerText.Text = CaveAiOfflineBrain.Answer(_project, QueryBox.Text, _library);
    }
}
