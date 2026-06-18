using System.Windows;
using System.Windows.Controls;

namespace CaveAiProForWindows.Views;

/// <summary>Simple single-line text prompt dialog.</summary>
internal static class ShareUrlPrompt
{
    public static string? Show(Window? owner, string title, string prompt, string? defaultText = null)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 560,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
        };

        var box = new TextBox
        {
            Text = defaultText ?? "",
            Margin = new Thickness(12, 8, 12, 8),
            Padding = new Thickness(8, 6, 8, 6),
        };

        string? result = null;
        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
        };
        ok.Click += (_, _) =>
        {
            result = box.Text?.Trim();
            dlg.DialogResult = true;
        };

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Padding = new Thickness(16, 6, 16, 6),
        };
        cancel.Click += (_, _) => dlg.DialogResult = false;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 0, 12, 12),
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(12, 0, 12, 4),
            TextWrapping = TextWrapping.Wrap,
        });
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(box);

        dlg.Content = root;
        return dlg.ShowDialog() == true ? result : null;
    }
}
