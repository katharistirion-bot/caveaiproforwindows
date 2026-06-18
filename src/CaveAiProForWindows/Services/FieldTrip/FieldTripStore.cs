using System.IO;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.FieldTrip;

public static class FieldTripStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "field-trips.json");

    public static FieldTripStoreFile Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return new FieldTripStoreFile();
            return JsonSerializer.Deserialize<FieldTripStoreFile>(File.ReadAllText(StorePath), JsonOptions)
                   ?? new FieldTripStoreFile();
        }
        catch
        {
            return new FieldTripStoreFile();
        }
    }

    public static void Save(FieldTripStoreFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    public static void Upsert(FieldTripDocument trip)
    {
        var store = Load();
        trip.UpdatedAt = DateTimeOffset.UtcNow;
        var idx = store.Trips.FindIndex(t => string.Equals(t.Id, trip.Id, StringComparison.Ordinal));
        if (idx >= 0)
            store.Trips[idx] = trip;
        else
            store.Trips.Add(trip);
        Save(store);
    }
}
