using System.Printing;
using System.Windows;
using System.Windows.Controls;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

public partial class PrintPreviewWindow : Window
{
    private readonly SurveyMapPrintRequest _request;

    public PrintPreviewWindow(SurveyMapPrintRequest request)
    {
        _request = request;
        InitializeComponent();
        RebuildDocument();
    }

    private PrintLayoutOptions CurrentLayoutOptions()
    {
        var paper = PrintPaperSize.A4;
        if (PaperSizeCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            paper = tag switch
            {
                "A2" => PrintPaperSize.A2,
                "A3" => PrintPaperSize.A3,
                _ => PrintPaperSize.A4,
            };
        }

        var orientation = PortraitRadio.IsChecked == true
            ? PageOrientation.Portrait
            : PageOrientation.Landscape;

        return new PrintLayoutOptions
        {
            PaperSize = paper,
            Orientation = orientation,
            FitMapToPage = FitToPageCheck.IsChecked != false,
        };
    }

    private void LayoutOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        RebuildDocument();
    }

    private void RebuildDocument()
    {
        PreviewViewer.Document = PrintLayoutService.Build(_request, CurrentLayoutOptions());
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewViewer.Document == null)
            return;

        var layout = CurrentLayoutOptions();
        var pd = new PrintDialog();
        try
        {
            pd.PrintTicket.PageOrientation = layout.Orientation;
            pd.PrintTicket.PageMediaSize = new PageMediaSize(PrintLayoutService.ToPageMediaSizeName(layout.PaperSize));
        }
        catch
        {
            /* ignore unsupported ticket fields */
        }

        if (pd.ShowDialog() != true)
            return;

        try
        {
            pd.PrintDocument(PreviewViewer.Document.DocumentPaginator, _request.JobName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
