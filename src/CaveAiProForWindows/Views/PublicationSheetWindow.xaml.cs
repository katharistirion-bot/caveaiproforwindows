using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.PublicationSheet;
using Microsoft.Win32;

namespace CaveAiProForWindows.Views;

public partial class PublicationSheetWindow : Window
{
    private readonly CaveProjectDocument _project;
    private readonly Action<CaveProjectDocument>? _persistBeforeRebuild;
    private byte[]? _lastPng;

    public PublicationSheetWindow(CaveProjectDocument project, Action<CaveProjectDocument>? persistBeforeRebuild = null)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _persistBeforeRebuild = persistBeforeRebuild;
        InitializeComponent();
        Title = $"Publication sheet — {CaveProjectDisplayNames.GetDisplayName(project)}";
        OfflineHintText.Text = CaveAiOfflineBrain.FormatStatusHintPanel(project);
        RebuildPreview();
    }

    private PublicationSheetOptions CurrentOptions()
    {
        var elevation = PublicationSheetElevationSource.LongProfile;
        if (ElevationSourceCombo.SelectedItem is ComboBoxItem { Tag: string tag } && tag == "Section")
            elevation = PublicationSheetElevationSource.Section;

        var quality = MapExportQuality.Standard;
        if (QualityCombo.SelectedItem is ComboBoxItem { Tag: string qTag } && qTag == "Print")
            quality = MapExportQuality.Print;

        return new PublicationSheetOptions
        {
            ElevationSource = elevation,
            Include3DOverview = Include3DCheck.IsChecked != false,
            Quality = quality,
        };
    }

    private void RebuildPreview()
    {
        StatusText.Text = "Building preview…";
        PreviewImage.Source = null;
        _lastPng = null;

        try
        {
            _persistBeforeRebuild?.Invoke(_project);
            OfflineHintText.Text = CaveAiOfflineBrain.FormatStatusHintPanel(_project);

            var previewOptions = CurrentOptions().WithQuality(MapExportQuality.Standard);
            var result = PublicationSheetComposer.TryCompose(_project, previewOptions);
            if (!result.IsSuccess)
            {
                StatusText.Text = result.ErrorMessage ?? "Preview unavailable.";
                return;
            }

            _lastPng = result.PngBytes;
            PreviewImage.Source = LoadPreviewImage(result.PngBytes!);
            StatusText.Text = "Preview ready. Refresh pulls latest sketch/vector data from the project. Use Print quality before final export.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static BitmapImage LoadPreviewImage(byte[] png)
    {
        using var ms = new MemoryStream(png);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private byte[]? BuildExportPng()
    {
        _persistBeforeRebuild?.Invoke(_project);
        var result = PublicationSheetComposer.TryCompose(_project, CurrentOptions());
        if (!result.IsSuccess)
        {
            MessageBox.Show(this, result.ErrorMessage ?? "Export failed.", "Publication sheet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return result.PngBytes;
    }

    private void LayoutOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;
        RebuildPreview();
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e) => RebuildPreview();

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var png = BuildExportPng();
        if (png == null)
            return;

        var safe = string.Join("_", _project.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Title = "Export publication sheet PNG",
            Filter = "PNG|*.png",
            FileName = $"{safe}_publication_sheet.png",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            File.WriteAllBytes(dlg.FileName, png);
            StatusText.Text = "PNG saved.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var png = BuildExportPng();
        if (png == null)
            return;

        var safe = string.Join("_", _project.Name.Split(Path.GetInvalidFileNameChars()));
        var dlg = new SaveFileDialog
        {
            Title = "Export publication sheet PDF",
            Filter = "PDF|*.pdf",
            FileName = $"{safe}_publication_sheet.pdf",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            CaveSurveyBookletExportService.SavePagesAsPdf(new[] { png }, dlg.FileName);
            StatusText.Text = "PDF saved.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
