using System.Collections.Generic;

namespace ZenithApp.Modules.RadialSwitcher;

public sealed class RadialSwitcherSettings
{
    public bool Enabled { get; set; } = true;
    public int HoldDelayMs { get; set; } = 220; // 150 - 450 ms
    public int ZoneCount { get; set; } = 8; // 4, 6 или 8 зон
    public bool AutoLaunchPinned { get; set; } = true;

    public List<RadialZoneConfig> CustomZones { get; set; } = new();

    public static readonly string[] DirectionNames8 =
    {
        "Вверх (Север)",
        "Вправо-вверх (СВ)",
        "Вправо (Восток)",
        "Вправо-вниз (ЮВ)",
        "Вниз (Юг)",
        "Влево-вниз (ЮЗ)",
        "Влево (Запад)",
        "Влево-вверх (СЗ)"
    };

    public static readonly string[] DirectionNames6 =
    {
        "Вверх",
        "Вправо-вверх",
        "Вправо-вниз",
        "Вниз",
        "Влево-вниз",
        "Влево-вверх"
    };

    public static readonly string[] DirectionNames4 =
    {
        "Вверх",
        "Вправо",
        "Вниз",
        "Влево"
    };

    public void EnsureZonesConfigured()
    {
        var count = ZoneCount is 4 or 6 or 8 ? ZoneCount : 8;
        ZoneCount = count;

        var names = count switch
        {
            4 => DirectionNames4,
            6 => DirectionNames6,
            _ => DirectionNames8
        };

        if (CustomZones == null)
        {
            CustomZones = new List<RadialZoneConfig>();
        }

        while (CustomZones.Count < count)
        {
            int idx = CustomZones.Count;
            CustomZones.Add(new RadialZoneConfig(idx, names[idx]));
        }

        if (CustomZones.Count > count)
        {
            CustomZones.RemoveRange(count, CustomZones.Count - count);
        }

        for (int i = 0; i < count; i++)
        {
            CustomZones[i].Index = i;
            CustomZones[i].DirectionName = names[i];
        }
    }
}
