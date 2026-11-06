using Wms.Domain.Services;

namespace Wms.Domain.Tests;

public class SlottingPolicyTests
{
    [Fact]
    public void ClassifyAbc_splits_80_95_and_treats_zero_as_c()
    {
        var cls = SlottingPolicy.ClassifyAbc(
        [
            ("a", 80), ("b", 15), ("c", 5),
        ]);
        Assert.Equal("A", cls["a"]);
        Assert.Equal("B", cls["b"]);
        Assert.Equal("C", cls["c"]);
        Assert.All(SlottingPolicy.ClassifyAbc([("x", 0), ("y", 0)]).Values, v => Assert.Equal("C", v));
        Assert.Equal("A", SlottingPolicy.ClassifyAbc([("hot", 90), ("rest", 10)])["hot"]);
    }

    [Fact]
    public void Fits_respects_cell_capacity()
    {
        Assert.True(SlottingPolicy.Fits(10, 4, 6));
        Assert.False(SlottingPolicy.Fits(10, 4, 6.1));
        Assert.True(SlottingPolicy.Fits(10, 10, 0));
        Assert.Equal(12, SlottingPolicy.UsedLiters([(2, 4), (4, 1)]));
    }

    [Fact]
    public void Heavy_items_prefer_lower_levels_and_affinity_shortens_score()
    {
        var high = SlottingPolicy.CandidateScore(8, 4, 1500, 10, false);
        var low = SlottingPolicy.CandidateScore(8, 1, 1500, 10, false);
        Assert.True(low < high);
        var withAff = SlottingPolicy.CandidateScore(8, 1, 200, 0, true);
        var without = SlottingPolicy.CandidateScore(8, 1, 200, 12, true);
        Assert.True(withAff < without);
    }

    [Fact]
    public void Distance_matches_previous_formula() =>
        Assert.Equal(16, SlottingPolicy.Distance(10, 3, 3));
}
