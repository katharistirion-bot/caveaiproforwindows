using System.IO;

using System.IO.Compression;

using System.Text.Json;

using CaveAiProForWindows.Models;

using CaveAiProForWindows.Services.ReferenceCatalog;



namespace CaveAiProForWindows.Services;



/// <summary>Creates a redacted diagnostic bundle (logs + ui-settings) for support.</summary>

public static class DiagnosticBundleExporter

{

    private static readonly string[] NeverExportFileNames =

    {

        "auth-token.dat",

        "auth-token.json",

    };



    public static string ExportToZip(string? destinationZipPath = null)

    {

        var dir = DiagnosticLogPaths.AppDataDirectory;

        Directory.CreateDirectory(dir);



        destinationZipPath ??= Path.Combine(

            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),

            $"CaveAiPro-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.zip");



        if (File.Exists(destinationZipPath))

            File.Delete(destinationZipPath);



        using var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);

        AddRedactedTextFileIfExists(zip, DiagnosticLogPaths.StartupLogPath, "startup.log");

        AddRedactedTextFileIfExists(zip, DiagnosticLogPaths.LastErrorPath, "last-error.txt");



        if (File.Exists(AppUiSettingsStore.SettingsPath))

        {

            var redacted = RedactUiSettings(File.ReadAllText(AppUiSettingsStore.SettingsPath));

            var entry = zip.CreateEntry("ui-settings-redacted.json");

            using var sw = new StreamWriter(entry.Open());

            sw.Write(redacted);

        }



        foreach (var log in Directory.EnumerateFiles(dir, "*.log"))

        {

            if (string.Equals(log, DiagnosticLogPaths.StartupLogPath, StringComparison.OrdinalIgnoreCase))

                continue;

            if (IsNeverExportFile(log))

                continue;

            AddRedactedTextFileIfExists(zip, log, Path.GetFileName(log));

        }



        ReferenceCatalogLightAnalytics.PersistSnapshot();

        if (File.Exists(ReferenceCatalogLightAnalytics.AnalyticsJsonPath))

            AddRedactedTextFileIfExists(zip, ReferenceCatalogLightAnalytics.AnalyticsJsonPath, "light-analytics.json");



        return destinationZipPath;

    }



    private static bool IsNeverExportFile(string path)

    {

        var name = Path.GetFileName(path);

        return NeverExportFileNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    }



    private static void AddRedactedTextFileIfExists(ZipArchive zip, string path, string entryName)

    {

        if (!File.Exists(path) || IsNeverExportFile(path))

            return;



        var redacted = DiagnosticLogRedactor.RedactFileContent(File.ReadAllText(path));

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);

        using var sw = new StreamWriter(entry.Open());

        sw.Write(redacted);

    }



    private static string RedactUiSettings(string json)

    {

        try

        {

            var doc = JsonSerializer.Deserialize<AppUiSettingsModel>(json);

            if (doc == null)

                return DiagnosticLogRedactor.RedactJsonSecrets(json);



            doc.CollaborationSharedProjectId = Redact(doc.CollaborationSharedProjectId);

            if (doc.AndroidSync.SyncFolderPath != null)

                doc.AndroidSync.SyncFolderPath = RedactPath(doc.AndroidSync.SyncFolderPath);

            var serialized = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

            return DiagnosticLogRedactor.RedactJsonSecrets(serialized);

        }

        catch

        {

            return DiagnosticLogRedactor.RedactJsonSecrets(json);

        }

    }



    private static string? Redact(string? value)

    {

        if (string.IsNullOrWhiteSpace(value))

            return value;

        if (value.Length <= 4)

            return "***";

        return value[..2] + "***" + value[^2..];

    }



    private static string RedactPath(string path)

    {

        var name = Path.GetFileName(path);

        return $".../{name}";

    }

}

