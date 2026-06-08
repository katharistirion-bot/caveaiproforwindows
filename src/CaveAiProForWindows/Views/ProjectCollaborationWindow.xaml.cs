using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.Collaboration;

namespace CaveAiProForWindows.Views;

public partial class ProjectCollaborationWindow : Window
{
    private readonly CaveProjectDocument _project;
    private readonly ProjectCollaborationClient _client = new();
    private readonly string? _sharedProjectId;

    public ProjectCollaborationWindow(CaveProjectDocument project, string? sharedProjectId, Window? owner)
    {
        _project = project;
        _sharedProjectId = sharedProjectId;
        InitializeComponent();
        if (owner != null)
            Owner = owner;
        var publishedId = LinkedLibraryCaveIdResolver.TryGet(project);
        HeaderText.Text = string.IsNullOrWhiteSpace(_sharedProjectId)
            ? $"Comments for «{project.Name}». Use Tools → Share for collaboration first."
            : $"Shared id {_sharedProjectId}" +
              (publishedId != null ? $" · published_caves/{publishedId}" : "") +
              " — comments notify team devices via push.";
        Loaded += async (_, _) => await RefreshCommentsAsync();
    }

    public static async Task ShareAndOpenAsync(CaveProjectDocument project, Window? owner)
    {
        var client = new ProjectCollaborationClient();
        try
        {
            var projectId = await client.ShareProjectAsync(
                project.Name,
                surveyJsonUrl: null,
                memberEmails: null,
                CancellationToken.None).ConfigureAwait(true);

            new ProjectCollaborationWindow(project, projectId, owner).ShowDialog();
            var settings = AppUiSettingsStore.LoadOrDefault();
            settings.CollaborationSharedProjectId = projectId;
            AppUiSettingsStore.Save(settings);
            AndroidDesktopSyncHub.NotifyCollaborationProjectChanged(projectId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Collaboration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private sealed class CommentRow
    {
        public required string DisplayLine { get; init; }
    }

    private async Task RefreshCommentsAsync()
    {
        if (string.IsNullOrWhiteSpace(_sharedProjectId))
        {
            CommentsList.ItemsSource = Array.Empty<CommentRow>();
            return;
        }

        try
        {
            var comments = await _client.ListCommentsAsync(_sharedProjectId).ConfigureAwait(true);
            CommentsList.ItemsSource = comments
                .OrderByDescending(c => c.CreatedAtMs)
                .Select(c => new CommentRow
                {
                    DisplayLine =
                        $"[{DateTimeOffset.FromUnixTimeMilliseconds(c.CreatedAtMs).LocalDateTime:g}] " +
                        $"{c.AuthorDisplayName}: {c.Text}",
                })
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Comments", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshCommentsAsync();

    private async void Post_Click(object sender, RoutedEventArgs e)
    {
        var text = CommentBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;
        if (string.IsNullOrWhiteSpace(_sharedProjectId))
        {
            MessageBox.Show(this, "Share the project first (Tools → Share for collaboration).", "Comments",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _client.PostCommentAsync(_sharedProjectId, text).ConfigureAwait(true);
            CommentBox.Clear();
            SnackbarService.Show(this, "Comment posted — push sent to registered mobile devices.");
            await RefreshCommentsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Post comment", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
