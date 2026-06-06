using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

/// <summary>Offline AI analytics dashboard: local algorithms on <see cref="CaveProjectDocument"/>.</summary>
public partial class AiAnalyticsView : UserControl
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project),
        typeof(CaveProjectDocument),
        typeof(AiAnalyticsView),
        new PropertyMetadata(null, OnProjectPropertyChanged));

    private AiAnalyticsViewModel? _viewModel;

    public CaveProjectDocument? Project
    {
        get => (CaveProjectDocument?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    public AiAnalyticsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureViewModel();
        _viewModel?.OnViewLoaded();
        if (ModelListBox != null)
            ModelListBox.SelectionChanged += ModelListBox_SelectionChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (ModelListBox != null)
            ModelListBox.SelectionChanged -= ModelListBox_SelectionChanged;
    }

    private void EnsureViewModel()
    {
        if (_viewModel != null || AiAnalyticsPanel == null)
            return;

        _viewModel = new AiAnalyticsViewModel();
        AiAnalyticsPanel.DataContext = _viewModel;
        _viewModel.SetProject(Project);
        _viewModel.SetSelectedToolTag(GetSelectedToolTag() ?? "Qc");
    }

    private static void OnProjectPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AiAnalyticsView v)
            return;
        v.EnsureViewModel();
        v._viewModel?.SetProject(v.Project);
    }

    private void ModelListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel?.SetSelectedToolTag(GetSelectedToolTag() ?? "Qc");
    }

    private string? GetSelectedToolTag()
    {
        if (ModelListBox?.SelectedItem is ListBoxItem li && li.Tag is string s)
            return s;
        return (ModelListBox?.SelectedItem as ListBoxItem)?.Tag as string;
    }

    private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsDataGrid?.SelectedItem is AiAnalyticsMetricRow row)
            _viewModel?.SelectStationFromRow(row);
    }
}
