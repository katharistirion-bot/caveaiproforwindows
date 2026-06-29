using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows.Views;

/// <summary>Offline survey intelligence dashboard on <see cref="CaveProjectDocument"/>.</summary>
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
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureViewModel();
        _viewModel?.OnViewLoaded();
    }

    private void EnsureViewModel()
    {
        if (_viewModel != null || AiAnalyticsPanel == null)
            return;

        _viewModel = new AiAnalyticsViewModel();
        AiAnalyticsPanel.DataContext = _viewModel;
        _viewModel.SetProject(Project);
    }

    private static void OnProjectPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not AiAnalyticsView v)
            return;
        v.EnsureViewModel();
        v._viewModel?.SetProject(v.Project);
    }

    private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsDataGrid?.SelectedItem is AiAnalyticsMetricRow row)
            _viewModel?.SelectStationFromRow(row);
    }
}
