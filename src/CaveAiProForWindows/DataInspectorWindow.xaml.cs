using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows;

public partial class DataInspectorWindow : Window
{
    public DataInspectorWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ShotsDataGrid != null)
            ShotsDataGrid.MouseDoubleClick += ShotsGrid_MouseDoubleClick;
        if (StationQcGrid != null)
            StationQcGrid.MouseDoubleClick += StationQcGrid_MouseDoubleClick;
    }

    private void ShotsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ShotsDataGrid?.SelectedItem is not ShotRecord row)
            return;
        var station = !string.IsNullOrWhiteSpace(row.FromStation) ? row.FromStation : row.ToStation;
        if (!string.IsNullOrWhiteSpace(station))
            SurveyWorkspaceNavigator.JumpToStation(station.Trim(), "DataInspector");
    }

    private void StationQcGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StationQcGrid?.SelectedItem is not StationQcRowViewModel qc || string.IsNullOrWhiteSpace(qc.Name))
            return;
        SurveyWorkspaceNavigator.JumpToStation(qc.Name.Trim(), "DataInspector");
    }

    private void ShotsDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;
        if (DataContext is not MainViewModel vm)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(vm.NotifySurveyDataEdited));
    }
}
