using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.ExpeditionShare;

namespace CaveAiProForWindows.Views;

public partial class ExpeditionSharePanel : UserControl
{
  private readonly ExpeditionShareRepository _repository = new();
  private bool _busy;
  private ExpeditionShareRepository.ExpeditionShareState _share = new() { Active = false };

  public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
      nameof(Project),
      typeof(CaveProjectDocument),
      typeof(ExpeditionSharePanel),
      new PropertyMetadata(null, OnProjectChanged));

  public CaveProjectDocument? Project
  {
      get => (CaveProjectDocument?)GetValue(ProjectProperty);
      set => SetValue(ProjectProperty, value);
  }

  public ExpeditionSharePanel()
  {
      InitializeComponent();
      Loaded += (_, _) => _ = RefreshAsync();
  }

  private static void OnProjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
  {
      if (d is ExpeditionSharePanel panel)
          _ = panel.RefreshAsync();
  }

  private async Task RefreshAsync()
  {
      ErrorText.Visibility = Visibility.Collapsed;
      try
      {
          _share = await _repository.LoadOwnShareAsync().ConfigureAwait(true);
      }
      catch (Exception ex)
      {
          _share = new ExpeditionShareRepository.ExpeditionShareState { Active = false };
          ShowError(ex.Message);
      }

      ApplyUi();
  }

  private void ApplyUi()
  {
      if (_share.Active)
      {
          var exit = _share.ExpectedExitAtMs is long ms && ms > 0
              ? $" · expected exit {DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime():g}"
              : "";
          StatusText.Text = $"Sharing {_share.CaveName} ({_share.Lat:F5}, {_share.Lon:F5}){exit}";
      }
      else
      {
          StatusText.Text = Project == null ? "Load a project with entrance GPS to share." : "Not sharing.";
      }

      StartButton.IsEnabled = !_busy && Project != null && !_share.Active;
      EndButton.IsEnabled = !_busy && _share.Active;
      ExpectedExitCombo.IsEnabled = !_busy && !_share.Active;
  }

  private void ShowError(string message)
  {
      ErrorText.Text = message;
      ErrorText.Visibility = Visibility.Visible;
  }

  private long? SelectedExpectedExitMs()
  {
      var hours = ExpectedExitCombo.SelectedIndex switch
      {
          1 => 4,
          2 => 8,
          3 => 12,
          _ => (int?)null,
      };
      return hours == null ? null : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + hours.Value * 3_600_000L;
  }

  private async void StartButton_Click(object sender, RoutedEventArgs e)
  {
      if (Project == null)
      {
          ShowError("Select a project first.");
          return;
      }

      var consent = MessageBox.Show(
          Window.GetWindow(this),
          "Share your approximate expedition position on the subscriber map?\n\nOther CaveAI Pro subscribers may see your cave name and entrance coordinates. This is not emergency search-and-rescue.",
          "Expedition share consent",
          MessageBoxButton.YesNo,
          MessageBoxImage.Warning);
      if (consent != MessageBoxResult.Yes)
          return;

      _busy = true;
      ApplyUi();
      ErrorText.Visibility = Visibility.Collapsed;
      try
      {
          var publishedDocId = ResolvePublishedDocId(Project);
          _share = await _repository.StartSharingAsync(Project, publishedDocId, SelectedExpectedExitMs()).ConfigureAwait(true);
      }
      catch (Exception ex)
      {
          ShowError(ex.Message);
      }
      finally
      {
          _busy = false;
          ApplyUi();
      }
  }

  private async void EndButton_Click(object sender, RoutedEventArgs e)
  {
      _busy = true;
      ApplyUi();
      ErrorText.Visibility = Visibility.Collapsed;
      try
      {
          _share = await _repository.EndSharingAsync().ConfigureAwait(true);
      }
      catch (Exception ex)
      {
          ShowError(ex.Message);
      }
      finally
      {
          _busy = false;
          ApplyUi();
      }
  }

  private static string? ResolvePublishedDocId(CaveProjectDocument? project)
  {
      if (project == null) return null;
      var fromHistory = CloudPublishHistoryStore.ForProject(project.Name ?? "")
          .FirstOrDefault()?.PublishedDocId;
      if (!string.IsNullOrWhiteSpace(fromHistory)) return fromHistory.Trim();
      return LinkedLibraryCaveIdResolver.TryGet(project);
  }
}