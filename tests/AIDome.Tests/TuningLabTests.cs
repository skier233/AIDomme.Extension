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
    public void Suggestions_AreWhatApplyWouldSet_OnTheSameScale()
    {
        Assert.All(TuningLab.Suggestions(Methods, []), m => Assert.Null(m.SuggestedPerMinute));
        // Palm for a minute is much harder than two minutes of Figure Eight, again and again.
        var history = Enumerable.Range(0, 30).Select(_ => Said("stim:1:time:120", "stim:4:time:60")).ToList();
        var rows = TuningLab.Suggestions(Methods, history);
        var palm = rows.Single(x => x.Id == 4);
        var eight = rows.Single(x => x.Id == 1);
        // Relative worth moves toward his answers, and the scale stays anchored to the points in use.
        Assert.True(palm.SuggestedPerMinute / eight.SuggestedPerMinute > palm.CurrentPerMinute / eight.CurrentPerMinute);
        var twoHand = rows.Single(x => x.Id == 12);
        Assert.InRange(twoHand.SuggestedPerMinute!.Value / twoHand.CurrentPerMinute, 0.33, 3);
        Assert.Equal(30, palm.Comparisons);
        var view = TuningLab.View(TuningLab.Stim, Methods, [], history, 7, 4, new Random(1));
        Assert.Equal(30, view.YourComparisons);
        Assert.Equal(0, view.OptimizeNeeds);
    }

    [Fact]
    public void Deals_AreComparedAtRealAmounts_AndAnchoredToMethods()
    {
        List<OfferDefinition> offers =
        [
            new() { Id = 1, Name = "Earn it", Wording = "Edge [amount] more time[s], then you may [what].", Kind = Sessions.OfferKind.EdgesNow, MinAmount = 3, MaxAmount = 10, PointsPerUnit = 4, ForOrgasm = true, Enabled = true },
            new() { Id = 3, Name = "Pay in days", Wording = "You may [what] for [amount] day[s] of denial.", Kind = Sessions.OfferKind.Denial, MinAmount = 1, MaxAmount = 7, PointsPerUnit = 14, ForOrgasm = true, Enabled = true },
            new() { Id = 9, Name = "Off", Wording = "x", Kind = Sessions.OfferKind.EdgesNow, MinAmount = 1, MaxAmount = 2, PointsPerUnit = 4, ForRest = true, Enabled = false },
        ];
        var keys = DealLab.Candidates(offers).Select(x => x.Key).ToList();
        Assert.Contains("offer:1:3:0", keys);
        Assert.Contains("offer:3:7:0", keys);
        Assert.DoesNotContain(keys, k => k.StartsWith("offer:9:"));
        Assert.Contains("stim:4:time:60", TuningLab.Keys(DealLab.Mode, Methods, [], offers));

        // Denial days are far harder for him than their price says: every time, he'd rather edge.
        var history = Enumerable.Range(0, 10).Select(_ => new TuningComparisonEntry { Mode = DealLab.Mode, WinnerKey = "offer:1:10:0", LoserKey = "offer:3:1:0", Strength = 3, UserId = 7 }).ToList();
        var view = DealLab.View(offers, Methods, history, 7, 4, new Random(3));
        var days = view.Deals.Single(x => x.Id == 3);
        Assert.True(days.Factor > 1.5, $"factor {days.Factor}");
        Assert.True(view.Deals.Single(x => x.Id == 1).Factor < 1);
        Assert.DoesNotContain(view.Deals, x => x.Id == 9);
        Assert.NotNull(view.Left);
        // Never the same deal at two amounts: the answer is obvious.
        for (int seed = 0; seed < 30; seed++)
        {
            var v = DealLab.View(offers, Methods, [], null, 4, new Random(seed));
            Assert.False(DealLab.Parse(v.Left!.Candidate.Key) is { } a && DealLab.Parse(v.Right!.Candidate.Key) is { } b && a.Id == b.Id);
        }
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
