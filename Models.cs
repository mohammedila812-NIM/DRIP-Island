using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IslandWin;

public sealed record AlertInfo(string Title, string Subtitle, Action Snooze);

public sealed class AlarmItem
{
    public int Hour { get; set; }
    public int Minute { get; set; }
    public DateTime LastFired { get; set; }

    [JsonIgnore]
    public string Label => DateTime.Today.AddHours(Hour).AddMinutes(Minute).ToString("h:mm tt");
}

public sealed class ReminderItem
{
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }

    [JsonIgnore]
    public string When => Due.ToString("h:mm tt");
}

public sealed class StoreData
{
    public List<AlarmItem> Alarms { get; set; } = new();
    public List<ReminderItem> Reminders { get; set; } = new();
}

/// <summary>Plain JSON persistence in %AppData%\IslandWin\state.json.</summary>
public static class Store
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IslandWin", "state.json");

    public static StoreData Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<StoreData>(File.ReadAllText(FilePath)) ?? new StoreData();
        }
        catch (Exception ex)
        {
            Log.Error("Store.Load", ex);
        }
        return new StoreData();
    }

    public static void Save(StoreData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            Log.Error("Store.Save", ex);
        }
    }
}
