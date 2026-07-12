using System.Windows;
using CaveAiProForWindows.Services.FollowPublisher;

namespace CaveAiProForWindows.Views;

public partial class FollowingPublishersWindow : Window
{
    private readonly FollowPublisherRepository _repository = new();

    public FollowingPublishersWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var rows = await _repository.ListFollowedAsync().ConfigureAwait(true);
            List.ItemsSource = rows;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Following", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Unfollow_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is not FollowPublisherRepository.FollowedPublisherRow row) return;
        try
        {
            await _repository.UnfollowAsync(row.PublisherUid).ConfigureAwait(true);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Following", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}