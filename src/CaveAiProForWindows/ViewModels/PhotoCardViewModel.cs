using System.Windows.Media.Imaging;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.ViewModels;

/// <summary>One thumbnail in the PHOTOS gallery.</summary>
public sealed class PhotoCardViewModel
{
    private PhotoCardViewModel(
        BitmapSource bitmap,
        string caption,
        string category,
        string groupKey,
        string? station,
        string sourceLabel,
        string? localPath)
    {
        Bitmap = bitmap;
        Caption = caption;
        Category = category;
        GroupKey = groupKey;
        Station = station;
        SourceLabel = sourceLabel;
        LocalPath = localPath;
    }

    public BitmapSource Bitmap { get; }
    public string Caption { get; }
    public string Category { get; }
    public string GroupKey { get; }
    public string? Station { get; }
    public string SourceLabel { get; }
    public string? LocalPath { get; }

    public string CategoryDisplay => string.IsNullOrEmpty(Station)
        ? Category
        : $"{Category} · {Station}";

    public string TooltipText
    {
        get
        {
            var lines = new System.Collections.Generic.List<string> { Caption, $"Category: {Category}" };
            if (!string.IsNullOrEmpty(Station))
                lines.Add($"Station: {Station}");
            lines.Add($"Source: {SourceLabel}");
            if (!string.IsNullOrEmpty(LocalPath))
                lines.Add($"Path: {LocalPath}");
            return string.Join("\n", lines);
        }
    }

    public static PhotoCardViewModel From(BackupPhotoEntry e) =>
        new(e.Bitmap, e.Caption, e.Category, e.GroupKey, e.Station, e.SourceLabel, e.LocalPath);
}
