using AIDome.Sessions;
using AIDome.Sessions.Plan;
using Xunit;

namespace AIDome.Tests;

/// <summary>His general ending preferences and the session's own limit, as her rules apply them.</summary>
public sealed class EndingPreferenceTests
{
    private static SessionState AtTheEnd(EndingPreferences endings, bool punishment = false, bool noRuin = false)
    {
        var s = SessionEngineTests.NewState(SessionMode.FullGuided);
        s.Contract = s.Contract with { Punishment = punishment, Limits = s.Contract.Limits with { NoRuin = noRuin } };
        s.Endings = endings;
        s.Budget = 100;
        s.Points = 100;
        return s;
    }

    [Fact]
    public void NeverDenied_LetsHimCumAtTheEnd_ButNotWhenHeAsksEarly()
    {
        var s = AtTheEnd(new() { Denial = DenialPreference.Never });
        s.Retaliation.Active = true;
        Assert.Equal(100, DommePermission.Instance.Chance(s, requested: false));
        s.Points = 40;
        Assert.True(DommePermission.Instance.Chance(s, requested: true) < 50);
    }

    [Fact]
    public void DenialPreference_MovesHerOdds_ThatWay()
    {
        double Odds(DenialPreference p) => DommePermission.Instance.Chance(AtTheEnd(new() { Denial = p }), requested: false);
        Assert.True(Odds(DenialPreference.Rarely) > Odds(DenialPreference.Normal));
        Assert.True(Odds(DenialPreference.Normal) > Odds(DenialPreference.Often));
        Assert.True(Odds(DenialPreference.Often) > Odds(DenialPreference.Mostly));
    }

    [Fact]
    public void HisCadence_MakesAnEarlyOrgasmUnlikely_AndALateOneLikely()
    {
        double Odds(double daysSince)
        {
            var s = AtTheEnd(new() { OrgasmEveryDays = 7 });
            s.HoursSinceLastOrgasmAtStart = daysSince * 24;
            return DommePermission.Instance.Chance(s, requested: false);
        }
        Assert.True(Odds(1) < Odds(6));
        Assert.True(Odds(6) < Odds(12));
    }

    [Fact]
    public void Ruins_FollowHisPreference_AndTonightsLimit()
    {
        Assert.Equal(0, EndingAuthority.RuinChance(AtTheEnd(new() { Ruins = RuinPreference.Never })));
        Assert.Equal(0, EndingAuthority.RuinChance(AtTheEnd(new() { Ruins = RuinPreference.PunishmentOnly })));
        Assert.True(EndingAuthority.RuinChance(AtTheEnd(new() { Ruins = RuinPreference.PunishmentOnly }, punishment: true)) > 0);
        Assert.True(EndingAuthority.RuinChance(AtTheEnd(new() { Ruins = RuinPreference.Often }))
            > EndingAuthority.RuinChance(AtTheEnd(new() { Ruins = RuinPreference.Sometimes })));
        Assert.Equal(0, EndingAuthority.RuinChance(AtTheEnd(new() { Ruins = RuinPreference.Often }, noRuin: true)));

        // No ruin deals where ruins are off, and an owed ruin waits.
        var off = AtTheEnd(new() { Ruins = RuinPreference.Never });
        off.OfferTemplates = [new() { Id = 6, Name = "Ruined, then", Wording = "You can cum, ruined.", Kind = OfferKind.RuinNow, MinAmount = 1, MaxAmount = 1, PointsPerUnit = 30, ForOrgasm = true }];
        Assert.Null(Offers.Make(off, OfferBuys.Orgasm, new FixedRandom(0.1), DateTime.UtcNow));
        off.OwedRuins = 1;
        var lean = new EndingLean { Draw = 0, RuinDraw = 0 };
        EndingAuthority.Refresh(off, lean, projected: false);
        Assert.Equal(EndingKind.Full, lean.Kind);
    }

    [Fact]
    public void EndingPreferences_AreValidated()
    {
        Assert.Throws<ArgumentException>(() => new EndingPreferences { OrgasmEveryDays = 90 }.Validate());
        Assert.Throws<ArgumentException>(() => new EndingPreferences { Ruins = (RuinPreference)9 }.Validate());
    }
}

public sealed class ManifestTests
{
    [Fact]
    public void HerSettings_LiveInTheirOwnCoveSettingsTab()
    {
        var manifest = new AIDome.AiDomeExtension().GetUIManifest();
        Assert.Contains(manifest.SettingsTabs, x => x.Key == AIDome.AiDomeExtension.SettingsTab);
        var panels = manifest.SettingsPanels.Where(x => x.TargetTab == AIDome.AiDomeExtension.SettingsTab).Select(x => x.ComponentName).ToList();
        Assert.Equal(["HerPersonaPanel", "EndingsPanel", "LengthsPanel", "SegmentsPanel", "TuningLabLinkPanel", "AiDomeSettingsPanel"], panels);
        // The tuning lab is a page of its own, reached from the settings tab rather than the nav.
        Assert.Contains(manifest.Pages, x => x.Route == "aidome-tuning" && x.ComponentName == "TuningLabPage" && !x.ShowInNav);
    }
}
