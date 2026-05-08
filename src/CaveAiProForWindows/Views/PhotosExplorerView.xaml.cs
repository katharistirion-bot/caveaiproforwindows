using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

/// <summary>
/// PHOTOS tab — global gallery scanning every JPEG/PNG in the active backup ZIP plus all station-attached photos
/// from the loaded project. Supports grouping (station / category / none), filename filter, and full-screen
/// LightBox on click.
/// </summary>
public partial class PhotosExplorerView : UserControl
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(PhotosExplorerView),
        new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty ZipPathProperty = DependencyProperty.Register(
        nameof(ZipPath),
        typeof(string),
        typeof(PhotosExplorerView),
        new PropertyMetadata(null, OnInputChanged));

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public string? ZipPath
    {
        get => (string?)GetValue(ZipPathProperty);
        set => SetValue(ZipPathProperty, value);
    }

    private List<PhotoCardViewModel> _allCards = new();

    public PhotosExplorerView()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PhotosExplorerView v && !Equals(e.NewValue, e.OldValue))
            v.Refresh();
    }

    private void Refresh()
    {
        EmptyBorder.Visibility = Visibility.Collapsed;
        EmptyText.Text = "";

        var entries = BackupPhotoIndexer.Build(Project, ZipPath);
        _allCards = entries.Select(PhotoCardViewModel.From).ToList();

        ApplyView();

        if (_allCards.Count == 0)
        {
            EmptyText.Text = string.IsNullOrWhiteSpace(ZipPath) && Project == null
                ? "Open a CaveAI backup (.zip / .json) to populate the gallery."
                : "No JPEG/PNG photos were found in the active backup. Open BACKUP CONTENTS to inspect raw archive entries.";
            EmptyBorder.Visibility = Visibility.Visible;
        }
    }

    private void ApplyView()
    {
        var filter = (FilterBox.Text ?? "").Trim();
        IEnumerable<PhotoCardViewModel> filtered = _allCards;
        if (filter.Length > 0)
        {
            filtered = _allCards.Where(c =>
                c.Caption.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (c.Station ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                c.SourceLabel.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var rows = filtered.ToList();
        SubtitleText.Text = _allCards.Count == 0
            ? "No photos in the current backup."
            : $"{rows.Count} of {_allCards.Count} photo(s) shown — click a thumbnail to view full-screen.";

        var view = new ListCollectionView(rows);
        var mode = GroupModeCombo.SelectedIndex;
        view.GroupDescriptions.Clear();
        if (mode == 0)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PhotoCardViewModel.GroupKey)));
        else if (mode == 1)
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PhotoCardViewModel.Category)));

        PhotosItemsControl.ItemsSource = view;
    }

    private void GroupModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyView();
    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyView();

    private void PhotoCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is PhotoCardViewModel vm)
        {
            var owner = Window.GetWindow(this);
            ImageLightBoxWindow.Show(vm.Bitmap, vm.TooltipText, owner);
        }
    }
}
