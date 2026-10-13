using System;
using System.Collections.Generic;

namespace Cleaner.Core.Services;

public readonly record struct TreemapRect(double X, double Y, double Width, double Height);

/// <summary>
/// Раскладка «квадратной» карты (squarified treemap): площадь каждого прямоугольника
/// пропорциональна значению. Значения должны идти по убыванию и быть положительными.
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapRect> Compute(IReadOnlyList<double> values, double width, double height)
    {
        var n = values.Count;
        var result = new TreemapRect[n];
        if (n == 0 || width <= 0 || height <= 0) return result;

        var total = 0.0;
        foreach (var v in values) total += Math.Max(v, 0);
        if (total <= 0) return result;

        var scale = width * height / total;
        var areas = new double[n];
        for (var i = 0; i < n; i++) areas[i] = Math.Max(values[i], 0) * scale;

        double x = 0, y = 0, w = width, h = height;
        var start = 0;

        while (start < n)
        {
            if (areas[start] <= 0 || w <= 0 || h <= 0) break;

            var side = Math.Min(w, h);
            var rowSum = areas[start];
            var best = Worst(rowSum, areas[start], areas[start], side);
            var end = start + 1;

            while (end < n && areas[end] > 0)
            {
                var newSum = rowSum + areas[end];
                var worst = Worst(newSum, areas[start], areas[end], side);
                if (worst > best) break;
                rowSum = newSum;
                best = worst;
                end++;
            }

            if (w >= h)
            {
                var stripWidth = rowSum / h;
                var cy = y;
                for (var k = start; k < end; k++)
                {
                    var cellHeight = areas[k] / stripWidth;
                    result[k] = new TreemapRect(x, cy, stripWidth, cellHeight);
                    cy += cellHeight;
                }
                x += stripWidth;
                w -= stripWidth;
            }
            else
            {
                var stripHeight = rowSum / w;
                var cx = x;
                for (var k = start; k < end; k++)
                {
                    var cellWidth = areas[k] / stripHeight;
                    result[k] = new TreemapRect(cx, y, cellWidth, stripHeight);
                    cx += cellWidth;
                }
                y += stripHeight;
                h -= stripHeight;
            }

            start = end;
        }

        return result;
    }

    private static double Worst(double sum, double max, double min, double side)
    {
        var s2 = side * side;
        return Math.Max(s2 * max / (sum * sum), sum * sum / (s2 * min));
    }
}
