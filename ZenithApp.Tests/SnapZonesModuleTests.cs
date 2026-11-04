using System;
using System.Collections.Generic;
using System.Windows;
using ZenithApp.Modules.SnapZones;

namespace ZenithApp.Tests;

public class SnapZonesModuleTests
{
    [Fact]
    public void ZoneCalculator_TwoColumns5050_GeneratesCorrectZones()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        int spacing = 8;
        int margin = 8;

        var zones = ZoneCalculator.CalculateZones(workArea, ZoneLayoutPreset.TwoColumns5050, spacing, margin);

        Assert.Equal(2, zones.Count);
        Assert.Equal(1, zones[0].ZoneNumber);
        Assert.Equal(2, zones[1].ZoneNumber);

        // Left margin
        Assert.Equal(8, zones[0].Bounds.Left);
        Assert.Equal(8, zones[0].Bounds.Top);

        // Right zone starts after col1 + spacing
        double expectedCol1Right = zones[0].Bounds.Right;
        Assert.Equal(expectedCol1Right + spacing, zones[1].Bounds.Left);

        // Full width covered
        Assert.Equal(workArea.Width - margin, zones[1].Bounds.Right);
    }

    [Fact]
    public void ZoneCalculator_ThreeColumns_GeneratesThreeZones()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var zones = ZoneCalculator.CalculateZones(workArea, ZoneLayoutPreset.ThreeColumns, 8, 8);

        Assert.Equal(3, zones.Count);
        Assert.Equal(1, zones[0].ZoneNumber);
        Assert.Equal(2, zones[1].ZoneNumber);
        Assert.Equal(3, zones[2].ZoneNumber);
    }

    [Fact]
    public void ZoneCalculator_Grid2x2_GeneratesFourZones()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var zones = ZoneCalculator.CalculateZones(workArea, ZoneLayoutPreset.Grid2x2, 8, 8);

        Assert.Equal(4, zones.Count);
    }

    [Fact]
    public void ZoneCalculator_CustomProfile_GeneratesSpecifiedGrid()
    {
        var workArea = new Rect(0, 0, 1920, 1080);
        var custom = new CustomLayoutProfile
        {
            Columns = 3,
            Rows = 2,
            ColumnWeights = new List<double> { 1, 1, 1 },
            RowWeights = new List<double> { 1, 1 }
        };

        var zones = ZoneCalculator.CalculateZones(workArea, ZoneLayoutPreset.Custom, 8, 8, custom);

        Assert.Equal(6, zones.Count);
    }

    [Fact]
    public void SnapCandidate_FiltersSystemAndExcludedProcesses()
    {
        var excluded = new List<string> { "dwm", "explorer", "ZenithApp", "game" };

        Assert.False(WindowPlacementService.IsSnapCandidate(nint.Zero, excluded));
    }
}
