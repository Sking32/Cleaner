using System.Linq;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class TreemapLayoutTests
{
    private static readonly double[] Values = { 6, 6, 4, 3, 2, 2, 1 };

    [Fact]
    public void Areas_Add_Up_To_Whole_Rectangle()
    {
        var rects = TreemapLayout.Compute(Values, 600, 400);
        Assert.Equal(Values.Length, rects.Count);
        Assert.Equal(600.0 * 400.0, rects.Sum(r => r.Width * r.Height), 4);
    }

    [Fact]
    public void Every_Rectangle_Is_Inside_Bounds()
    {
        const double eps = 1e-6;
        foreach (var r in TreemapLayout.Compute(Values, 600, 400))
        {
            Assert.True(r.X >= -eps && r.Y >= -eps);
            Assert.True(r.X + r.Width <= 600 + eps);
            Assert.True(r.Y + r.Height <= 400 + eps);
        }
    }

    [Fact]
    public void Area_Is_Proportional_To_Value()
    {
        var rects = TreemapLayout.Compute(Values, 600, 400);
        var total = Values.Sum();
        for (var i = 0; i < Values.Length; i++)
            Assert.Equal(Values[i] / total * 600 * 400, rects[i].Width * rects[i].Height, 4);
    }

    [Fact]
    public void Empty_Or_Invalid_Input_Gives_No_Crash()
    {
        Assert.Empty(TreemapLayout.Compute(new double[0], 100, 100));
        Assert.All(TreemapLayout.Compute(new double[] { 1, 2 }, 0, 100), r => Assert.Equal(0, r.Width));
    }
}
