using System.Windows;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.SurveyCloud;

namespace CaveAiProForWindows.Views;

public partial class SurveyCloudProjectPickerWindow : Window
{
    private readonly FirebaseIdToken _token;
    private List<SurveyCloudPickerRow> _rows = [];

    public SurveyCloudProjectMeta? SelectedMeta { get; private set; }

    private SurveyCloudProjectPickerWindow(FirebaseIdToken token)
    {
        _token = token;
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    public static Task<SurveyCloudProjectMeta?> TryPickAsync(Window? owner, FirebaseIdToken token)
    {
        var dlg = new SurveyCloudProjectPickerWindow(token) { Owner = owner };
        return Task.FromResult(dlg.ShowDialog() == true ? dlg.SelectedMeta : null);
    }

    private async Task ReloadAsync()
    {
        try
        {
            var list = await SurveyCloudProjectService.ListOwnerProjectsAsync(_token).ConfigureAwait(true);
            _rows = list
                .Select(m => new SurveyCloudPickerRow(m))
                .ToList();
            ProjectsList.ItemsSource = _rows;
            if (_rows.Count > 0)
                ProjectsList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Survey Cloud", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not SurveyCloudPickerRow row)
        {
            MessageBox.Show(this, "Select a survey project.", "Survey Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedMeta = row.Meta;
        DialogResult = true;
        Close();
    }

    private sealed class SurveyCloudPickerRow
    {
        public SurveyCloudProjectMeta Meta { get; }

        public SurveyCloudPickerRow(SurveyCloudProjectMeta meta)
        {
            Meta = meta;
        }

        public string DisplayLine
        {
            get
            {
                var when = Meta.UpdatedAtMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(Meta.UpdatedAtMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                    : "unknown time";
                return $"{Meta.CaveName} — {Meta.ShotCount} shots — {when} ({Meta.PlatformOrigin})";
            }
        }
    }
}