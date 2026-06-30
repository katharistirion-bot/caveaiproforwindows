using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace CaveAiProForWindows.Services;

public static class ShellLayoutStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CaveAiProForWindows", "shell-layout.json");

    public static void ApplyTo(
        ColumnDefinition recentFilesColumn,
        ColumnDefinition projectsColumn,
        TabControl surveyTabs,
        object? reserved,
        Action<string>? selectProjectByName)
    {
        _ = reserved;
        try
        {
            if (!File.Exists(StorePath)) return;
            var json = File.ReadAllText(StorePath);
            var dto = JsonSerializer.Deserialize<ShellLayoutDto>(json, JsonOpts);
            if (dto == null) return;

            if (dto.RecentFilesWidth is > 80 and < 600)
                recentFilesColumn.Width = new GridLength(dto.RecentFilesWidth.Value);
            if (dto.ProjectsWidth is > 120 and < 800)
                projectsColumn.Width = new GridLength(dto.ProjectsWidth.Value);
            if (dto.SelectedSurveyTabIndex is >= 0 and var tabIdx && tabIdx < surveyTabs.Items.Count)
                surveyTabs.SelectedIndex = tabIdx;

            if (!string.IsNullOrWhiteSpace(dto.SelectedProjectName))
                selectProjectByName?.Invoke(dto.SelectedProjectName);
        }
        catch { }
    }

    public static ShellLayoutDto CaptureFrom(
        ColumnDefinition recentFilesColumn,
        ColumnDefinition projectsColumn,
        TabControl surveyTabs,
        string? selectedProjectName)
    {
        return new ShellLayoutDto
        {
            RecentFilesWidth = recentFilesColumn.Width.IsAbsolute ? recentFilesColumn.Width.Value : 220,
            ProjectsWidth = projectsColumn.Width.IsAbsolute ? projectsColumn.Width.Value : 280,
            SelectedSurveyTabIndex = surveyTabs.SelectedIndex,
            SelectedProjectName = selectedProjectName,
        };
    }

    public static void Save(ShellLayoutDto dto)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(dto, JsonOpts));
        }
        catch { }
    }
}

public sealed class ShellLayoutDto
{
    public double? RecentFilesWidth { get; set; }
    public double? ProjectsWidth { get; set; }
    public int? SelectedSurveyTabIndex { get; set; }
    public string? SelectedProjectName { get; set; }
}