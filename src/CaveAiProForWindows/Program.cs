using System;
using System.Windows;
using CaveAiProForWindows.Services;
using Velopack;

namespace CaveAiProForWindows;

/// <summary>
/// Custom entry point. Velopack install/update hooks run before WPF startup on sideload builds only;
/// Microsoft Store builds skip Velopack because the Store handles install and updates.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (DistributionChannel.UseVelopackBootstrap)
        {
            VelopackApp.Build()
                .SetArgs(args)
                .Run();
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
