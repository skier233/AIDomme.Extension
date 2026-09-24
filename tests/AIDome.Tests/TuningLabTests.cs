using AIDome.Data;
using AIDome.Services;
using Xunit;

namespace AIDome.Tests;

/// <summary>The side-by-side calibration lab ported from the original app.</summary>
public sealed class TuningLabTests
{
    private static readonly List<StimMethod> Methods =
    [
        new() { Id = 1, Name = "Figure Eight", Description = "Perform the figure eight on your cock.", Intensity = 6, TortLevel = 4, Closeness = 7, RequiresLube = true },
        new() { Id = 4, Name = "Palm", Description = "Palm your cock.", Intensity = 9, TortLevel = 9, Closeness = 1, RequiresLube = true },
        new() { Id = 12, Name = "2-Hand Stroke", Description = "Stroke up and down your cock with 2 hands.", Intensity = 7, TortLevel = 2, Closeness = 8, RequiresLube = true },
    ];

    private static TuningComparisonEntry Said(string easier, string harder, int strength = 3, int? user = 7)
        => new() { Mode = TuningLab.Stim, WinnerKey = easier, LoserKey = harder, Strength = strength, UserId = user };

    [Fact]
    public void Candidates_AreTimesForEveryMethod_AndEdgesOnlyWhereTheyCanBeReached()
    {
        var keys = TuningLab.Candidates(TuningLab.Stim, Methods, []).Select(x => x.Key).ToList();
        Assert.Contains("stim:4:time:60", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("stim:4:edge"));
        Assert.Contains("stim:1:edge:3", keys);
    }

    [Fact]
    public void HisAnswers_MoveWhatAMethodIsWorth()
    {
        var before = TuningLab.View(TuningLab.Stim, Methods, [], [], 7, 4, new Random(1)).Methods.Single(x => x.Id == 4);
        // Palm for a minute is much harder than two minutes of Figure Eight, again and again.
        var history = Enumerable.Range(0, 12).Select(_ => Said("stim:1:time:120", "stim:4:time:60")).ToList();
        var view = TuningLab.View(TuningLab.Stim, Methods, [], history, 7, 4, new Random(1));
        var after = view.Methods.Single(x => x.Id == 4);
        Assert.True(after.LearnedPerMinute > before.LearnedPerMinute * 1.2, $"{before.LearnedPerMinute} -> {after.LearnedPerMinute}");
        Assert.Equal(12, after.Comparisons);
        Assert.Equal(12, view.YourComparisons);
        Assert.Equal(13, view.OptimizeNeeds);
    }

    [Fact]
    public void TheNextMatchup_IsTwoDifferentThings_AndNeverAnObviousOne()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            var view = TuningLab.View(TuningLab.Stim, Methods, [], [], null, 4, new Random(seed));
            var (a, b) = (view.Left!.Candidate.Key, view.Right!.Candidate.Key);
            Assert.NotEqual(a, b);
            var (pa, pb) = (a.Split(':'), b.Split(':'));
            // The same method and kind at two amounts: the answer is obvious.
            Assert.False(pa[1] == pb[1] && pa[2] == pb[2], $"{a} vs {b}");
            Assert.Equal(1, view.Left.HarderChance + view.Right.HarderChance, 6);
        }
    }

    [Fact]
    public void Endings_AreComparedWithEachOther_AndRanked()
    {
        List<PunishmentEnder> enders =
        [
            new() { Id = 1, Name = "Forced", AnnouncementText = "You're going to cum.", Severity = 7, EndsInOrgasm = true },
            new() { Id = 2, Name = "Denied", AnnouncementText = "No.", Severity = 5 },
        ];
        var history = Enumerable.Range(0, 6).Select(_ => new TuningComparisonEntry { Mode = TuningLab.Ender, WinnerKey = "ender:Forced", LoserKey = "ender:Denied", Strength = 3 }).ToList();
        var view = TuningLab.View(TuningLab.Ender, Methods, enders, history, null, 4, new Random(2));
        Assert.Equal("ender:Denied", view.Enders[0].Key);
        Assert.Empty(view.Methods);
    }
}
