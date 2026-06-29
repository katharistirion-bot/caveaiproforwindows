using System.Globalization;
using System.Windows;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Views;

/// <summary>Manual four-corner geo bounds for X-Ray (saved to extensionData).</summary>
public partial class XRayManualCalibrationWindow : Window
{
    private readonly CaveProjectDocument _project;

    public XRayManualCalibrationWindow(CaveProjectDocument project, XRayBackdropMetadata? seed)
    {
        _project = project;
        InitializeComponent();
        Title = $"X-Ray geo calibration — {project.Name}";
        if (seed is { IsValid: true } s)
        {
            MinLatBox.Text = s.MinLat.ToString("F6", CultureInfo.InvariantCulture);
            MaxLatBox.Text = s.MaxLat.ToString("F6", CultureInfo.InvariantCulture);
            MinLonBox.Text = s.MinLon.ToString("F6", CultureInfo.InvariantCulture);
            MaxLonBox.Text = s.MaxLon.ToString("F6", CultureInfo.InvariantCulture);
        }
        else if (project.Lat is double la && project.Lon is double lo)
        {
            const double span = 0.002;
            MinLatBox.Text = (la - span).ToString("F6", CultureInfo.InvariantCulture);
            MaxLatBox.Text = (la + span).ToString("F6", CultureInfo.InvariantCulture);
            MinLonBox.Text = (lo - span).ToString("F6", CultureInfo.InvariantCulture);
            MaxLonBox.Text = (lo + span).ToString("F6", CultureInfo.InvariantCulture);
        }
    }

    public static bool TryShowDialog(Window? owner, CaveProjectDocument project)
    {
        XRayBackdropMetadata? seed = XRayManualBoundsStore.TryRead(project, out var manual) && manual.IsValid
            ? manual
            : XRayBackdropMetadataParser.TryRead(project);
        var dlg = new XRayManualCalibrationWindow(project, seed) { Owner = owner };
        return dlg.ShowDialog() == true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseBounds(out var bounds))
            return;
        XRayManualBoundsStore.Save(_project, bounds);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private bool TryParseBounds(out XRayBackdropMetadata bounds)
    {
        bounds = new XRayBackdropMetadata(0, 0, 0, 0, XRayManualBoundsStore.ExtensionKey);
        if (!double.TryParse(MinLatBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minLat) ||
            !double.TryParse(MaxLatBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLat) ||
            !double.TryParse(MinLonBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minLon) ||
            !double.TryParse(MaxLonBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxLon))
        {
            MessageBox.Show(this, "Enter valid WGS-84 min/max lat/lon values.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        bounds = new XRayBackdropMetadata(minLat, maxLat, minLon, maxLon, XRayManualBoundsStore.ExtensionKey);
        if (!bounds.IsValid)
        {
            MessageBox.Show(this, "Bounds invalid — check min < max and lat/lon ranges.", Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}
