using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ZenithApp.Modules.SnapZones;

public sealed class ZoneRect
{
    public int ZoneNumber { get; set; }
    public Rect Bounds { get; set; } // Physical pixel coordinates in virtual screen space
}

public static class ZoneCalculator
{
    public static List<ZoneRect> CalculateZones(
        Rect workArea, 
        ZoneLayoutPreset preset, 
        int spacing, 
        int margin,
        CustomLayoutProfile? customProfile = null)
    {
        var result = new List<ZoneRect>();

        double left = workArea.Left + margin;
        double top = workArea.Top + margin;
        double availableW = Math.Max(100, workArea.Width - (margin * 2));
        double availableH = Math.Max(100, workArea.Height - (margin * 2));

        switch (preset)
        {
            case ZoneLayoutPreset.TwoColumns5050:
                result.AddRange(GenerateColumns(left, top, availableW, availableH, spacing, new[] { 1.0, 1.0 }));
                break;

            case ZoneLayoutPreset.ThreeColumns:
                result.AddRange(GenerateColumns(left, top, availableW, availableH, spacing, new[] { 1.0, 1.0, 1.0 }));
                break;

            case ZoneLayoutPreset.TwoColumns6633:
                result.AddRange(GenerateColumns(left, top, availableW, availableH, spacing, new[] { 2.0, 1.0 }));
                break;

            case ZoneLayoutPreset.TwoColumns3366:
                result.AddRange(GenerateColumns(left, top, availableW, availableH, spacing, new[] { 1.0, 2.0 }));
                break;

            case ZoneLayoutPreset.TwoRows:
                result.AddRange(GenerateRows(left, top, availableW, availableH, spacing, new[] { 1.0, 1.0 }));
                break;

            case ZoneLayoutPreset.Grid2x2:
                result.AddRange(GenerateGrid(left, top, availableW, availableH, spacing, 2, 2, new[] { 1.0, 1.0 }, new[] { 1.0, 1.0 }));
                break;

            case ZoneLayoutPreset.PriorityGrid:
                // Main column (50%) + Right two stacked rows (25% + 25%)
                double col1W = Math.Round((availableW - spacing) * 0.5);
                double col2W = availableW - spacing - col1W;
                double rowH = Math.Round((availableH - spacing) * 0.5);
                double row2H = availableH - spacing - rowH;

                result.Add(new ZoneRect { ZoneNumber = 1, Bounds = new Rect(left, top, col1W, availableH) });
                result.Add(new ZoneRect { ZoneNumber = 2, Bounds = new Rect(left + col1W + spacing, top, col2W, rowH) });
                result.Add(new ZoneRect { ZoneNumber = 3, Bounds = new Rect(left + col1W + spacing, top + rowH + spacing, col2W, row2H) });
                break;

            case ZoneLayoutPreset.CenterSingle:
                double singleW = Math.Round(availableW * 0.7);
                double singleH = Math.Round(availableH * 0.85);
                double sLeft = left + (availableW - singleW) / 2.0;
                double sTop = top + (availableH - singleH) / 2.0;
                result.Add(new ZoneRect { ZoneNumber = 1, Bounds = new Rect(sLeft, sTop, singleW, singleH) });
                break;

            case ZoneLayoutPreset.Custom:
                if (customProfile != null && customProfile.Columns > 0 && customProfile.Rows > 0)
                {
                    var colWeights = customProfile.ColumnWeights.Count == customProfile.Columns 
                        ? customProfile.ColumnWeights 
                        : Enumerable.Repeat(1.0, customProfile.Columns).ToList();

                    var rowWeights = customProfile.RowWeights.Count == customProfile.Rows 
                        ? customProfile.RowWeights 
                        : Enumerable.Repeat(1.0, customProfile.Rows).ToList();

                    result.AddRange(GenerateGrid(left, top, availableW, availableH, spacing, customProfile.Columns, customProfile.Rows, colWeights, rowWeights));
                }
                else
                {
                    result.AddRange(GenerateColumns(left, top, availableW, availableH, spacing, new[] { 1.0, 1.0 }));
                }
                break;
        }

        // Assign 1-based sequential numbers
        for (int i = 0; i < result.Count; i++)
        {
            result[i].ZoneNumber = i + 1;
        }

        return result;
    }

    private static List<ZoneRect> GenerateColumns(double left, double top, double width, double height, int spacing, IList<double> weights)
    {
        var list = new List<ZoneRect>();
        int cols = weights.Count;
        double totalSpacing = spacing * (cols - 1);
        double netWidth = Math.Max(10, width - totalSpacing);
        double totalWeight = weights.Sum();

        double curX = left;
        for (int i = 0; i < cols; i++)
        {
            double colW = Math.Round(netWidth * (weights[i] / totalWeight));
            if (i == cols - 1)
            {
                colW = (left + width) - curX; // Snap remainder
            }

            list.Add(new ZoneRect
            {
                Bounds = new Rect(curX, top, Math.Max(20, colW), height)
            });

            curX += colW + spacing;
        }

        return list;
    }

    private static List<ZoneRect> GenerateRows(double left, double top, double width, double height, int spacing, IList<double> weights)
    {
        var list = new List<ZoneRect>();
        int rows = weights.Count;
        double totalSpacing = spacing * (rows - 1);
        double netHeight = Math.Max(10, height - totalSpacing);
        double totalWeight = weights.Sum();

        double curY = top;
        for (int i = 0; i < rows; i++)
        {
            double rowH = Math.Round(netHeight * (weights[i] / totalWeight));
            if (i == rows - 1)
            {
                rowH = (top + height) - curY;
            }

            list.Add(new ZoneRect
            {
                Bounds = new Rect(left, curY, width, Math.Max(20, rowH))
            });

            curY += rowH + spacing;
        }

        return list;
    }

    private static List<ZoneRect> GenerateGrid(
        double left, double top, double width, double height, int spacing, 
        int cols, int rows, IList<double> colWeights, IList<double> rowWeights)
    {
        var list = new List<ZoneRect>();

        double totalColSpacing = spacing * (cols - 1);
        double netWidth = Math.Max(10, width - totalColSpacing);
        double totalColWeight = colWeights.Sum();

        double totalRowSpacing = spacing * (rows - 1);
        double netHeight = Math.Max(10, height - totalRowSpacing);
        double totalRowWeight = rowWeights.Sum();

        double curY = top;
        for (int r = 0; r < rows; r++)
        {
            double rowH = Math.Round(netHeight * (rowWeights[r] / totalRowWeight));
            if (r == rows - 1) rowH = (top + height) - curY;

            double curX = left;
            for (int c = 0; c < cols; c++)
            {
                double colW = Math.Round(netWidth * (colWeights[c] / totalColWeight));
                if (c == cols - 1) colW = (left + width) - curX;

                list.Add(new ZoneRect
                {
                    Bounds = new Rect(curX, curY, Math.Max(20, colW), Math.Max(20, rowH))
                });

                curX += colW + spacing;
            }

            curY += rowH + spacing;
        }

        return list;
    }
}
