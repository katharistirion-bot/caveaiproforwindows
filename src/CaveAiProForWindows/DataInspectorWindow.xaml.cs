using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CaveAiProForWindows.ViewModels;

namespace CaveAiProForWindows;

public partial class DataInspectorWindow : Window
{
    public DataInspectorWindow()
    {
        InitializeComponent();
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
