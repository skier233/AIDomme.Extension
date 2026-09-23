using System.Text.Json;
using AIDome.Data;
using AIDome.Sessions;
using AIDome.Speech;
using AIDome.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record StakeView(LedgerKind Kind, int Amount, int Severity, string Description);

public sealed record CommitmentView(Guid Id, CommitmentStatus Status, CommitmentTerms Terms, string Description, IReadOnlyList<StakeView> Stakes,
    string Response, bool Countered, DateTime CreatedAtUtc, DateTime? AcceptedAtUtc, DateTime? EndsAtUtc, int? SessionId,
    DateTime? ResolvedAtUtc, string? Resolution);

public sealed record LedgerEntryView(int Id, LedgerKind Kind, int Amount, int Remaining, int Severity, LedgerOrigin Origin, string Reason,
    string Description, DateTime CreatedAtUtc, DateTime? EndsAtUtc, LedgerStatus Status, DateTime? ServedAtUtc);

public sealed record FollowUpAnswer(string Value, string Label);

public sealed record FollowUpView(Guid Id, FollowUpKind Kind, int? SessionId, DateTime OccurredAtUtc, string Question, string Observed,
    IReadOnlyList<FollowUpAnswer> Answers);

/// <summary>Promises, what is owed, and the questions she is waiting on.</summary>
public sealed record LedgerView(IReadOnlyList<CommitmentView> Commitments, IReadOnlyList<LedgerEntryView> Entries, IReadOnlyList<FollowUpView> FollowUps);

public sealed record CommitmentProposal(CommitmentTerms Terms);
public sealed record FollowUpAnswerRequest(string Answer);

/// <summary>
/// The punishment ledger, commitments and follow-up questions for one account. Obligations are
/// folded into a session when it starts and settled when it ends; breaches are found from the facts
/// (tracker events and session state) whenever the account is next seen, so closing Cove or the
/// browser never makes one disappear.
/// </summary>
public sealed class LedgerStore(GuidedDataFactory data, SessionHost host)
{
    /// <summary>A session paused by the user this long is treated as interrupted.</summary>
    public static readonly TimeSpan AbandonedPauseAfter = TimeSpan.FromHours(12);
    /// <summary>An interrupted session older than this is not resumed, even when nothing went wrong.</summary>
    public static readonly TimeSpan ResumableFor = TimeSpan.FromHours(6);
    private static readonly JsonSerializerOptions Json = GuidedRecovery.Json;
    private const int MaxActiveCommitments = 5;

    private static readonly TimeSpan ReconcileEvery = TimeSpan.FromSeconds(5);
    private DateTime lastReconciledUtc = DateTime.MinValue;

    private DateTime Now => host.Engine.Clock.UtcNow;

    // ── Reads ──

    public async Task<LedgerView> ViewAsync(CancellationToken ct)
    {
        await using var db = data.Open();
        var recent = Now.AddDays(-30);
        var commitments = await db.Commitments.AsNoTracking()
            .Where(x => x.Status == CommitmentStatus.Pending || x.Status == CommitmentStatus.Active || (x.ResolvedAtUtc != null && x.ResolvedAtUtc > recent))
            .OrderByDescending(x => x.CreatedAtUtc).Take(20).ToListAsync(ct);
        var entries = await db.Ledger.AsNoTracking()
            .Where(x => x.Status != LedgerStatus.Served || x.ServedAtUtc > recent)
            .OrderBy(x => x.Status == LedgerStatus.Served).ThenByDescending(x => x.CreatedAtUtc).Take(40).ToListAsync(ct);
        return new(commitments.Select(View).ToList(), entries.Select(View).ToList(), await OpenFollowUpsAsync(db, ct));
    }

    public async Task<IReadOnlyList<FollowUpView>> OpenFollowUpsAsync(CancellationToken ct)
    {
        await using var db = data.Open();
        return await OpenFollowUpsAsync(db, ct);
    }

    private static async Task<IReadOnlyList<FollowUpView>> OpenFollowUpsAsync(GuidedData db, CancellationToken ct)
        => (await db.FollowUps.AsNoTracking().Where(x => x.Status == FollowUpStatus.Open).OrderBy(x => x.OccurredAtUtc).ToListAsync(ct))
            .Select(View).ToList();

    public static CommitmentView View(CommitmentRecord c) => new(c.Id, c.Status, c.Terms, c.Terms.Describe(),
        StakesOf(c).Select(x => new StakeView(x.Kind, x.Amount, x.Severity, x.Describe())).ToList(), c.Response, c.Countered, c.CreatedAtUtc,
        c.AcceptedAtUtc, c.EndsAtUtc, c.SessionId, c.ResolvedAtUtc, c.Resolution);

    public static LedgerEntryView View(LedgerEntry e) => new(e.Id, e.Kind, e.Amount, e.Remaining, e.Severity, e.Origin, e.Reason,
        new Stake(e.Kind, e.Kind == LedgerKind.Denial || e.Status == LedgerStatus.Served ? e.Amount : Math.Max(1, e.Remaining), e.Severity).Describe(),
        e.CreatedAtUtc, e.EndsAtUtc, e.Status, e.ServedAtUtc);

    public static FollowUpView View(FollowUpRecord f) => f.Kind == FollowUpKind.UnauthorizedOrgasm
        ? new(f.Id, f.Kind, f.SessionId, f.OccurredAtUtc, "You came without my permission. Was it an accident?", f.Observed,
            [new("accident", "It was an accident"), new("intent", "I did it on purpose")])
        : new(f.Id, f.Kind, f.SessionId, f.OccurredAtUtc, "Your session stopped with nobody here. What happened?", f.Observed,
            [new("interrupted", "Something interrupted me"), new("quit", "I stopped on purpose"),
             new("came-accident", "I came (an accident)"), new("came-intent", "I came (on purpose)")]);

    private static List<Stake> StakesOf(CommitmentRecord c) => JsonSerializer.Deserialize<List<Stake>>(c.Stakes, Json) ?? [];

    // ── Commitments ──

    public async Task ProposeAsync(CommitmentTerms proposed, CancellationToken ct)
    {
        if (host.Active) throw new InvalidOperationException("Make your promises before a session starts.");
        var terms = (proposed ?? throw new ArgumentException("Terms are required.")).Validate();
        await host.PrepareIdleAsync(ct);
        await using var db = data.Open();
        var now = Now;
        if (await db.Commitments.CountAsync(x => x.Status == CommitmentStatus.Active, ct) >= MaxActiveCommitments)
            throw new InvalidOperationException("You already have enough promises to keep.");
        int broken = await db.Commitments.CountAsync(x => x.Status == CommitmentStatus.Broken && x.ResolvedAtUtc > now.AddDays(-30), ct);
        int mean = (await host.ResolvedDefaultsAsync(ct)).Mean;
        var response = CommitmentRules.Respond(terms, mean, broken, host.Engine.Random);
        foreach (var pending in await db.Commitments.Where(x => x.Status == CommitmentStatus.Pending).ToListAsync(ct))
        {
            pending.Status = CommitmentStatus.Declined;
            pending.ResolvedAtUtc = now;
            pending.Resolution = "Replaced by a new promise.";
        }
        var t = response.Terms;
        db.Add(new CommitmentRecord
        {
            Id = Guid.NewGuid(), Status = CommitmentStatus.Pending, Scope = t.Scope, Days = t.Days, NoOrgasm = t.NoOrgasm, Long = t.Long,
            Intense = t.Intense, Torturous = t.Torturous, Mean = t.Mean, Teasing = t.Teasing, Note = t.Note,
            Stakes = JsonSerializer.Serialize(response.Stakes, Json), Response = Truncate(host.Speak(response.Say), 1000), Countered = response.Countered,
            Proposed = response.Countered ? JsonSerializer.Serialize(terms, Json) : null, CreatedAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task DecideAsync(Guid id, bool accept, CancellationToken ct)
    {
        if (host.Active) throw new InvalidOperationException("Settle promises before a session starts.");
        await host.PrepareIdleAsync(ct);
        await using var db = data.Open();
        var c = await db.Commitments.SingleOrDefaultAsync(x => x.Id == id && x.Status == CommitmentStatus.Pending, ct)
            ?? throw new InvalidOperationException("That promise is no longer waiting for an answer.");
        var now = Now;
        if (accept)
        {
            if (await db.Commitments.CountAsync(x => x.Status == CommitmentStatus.Active, ct) >= MaxActiveCommitments)
                throw new InvalidOperationException("You already have enough promises to keep.");
            c.Status = CommitmentStatus.Active;
            c.AcceptedAtUtc = c.StartsAtUtc = now;
            c.EndsAtUtc = c.Scope == CommitmentScope.Days ? now.AddDays(c.Days) : null;
        }
        else
        {
            c.Status = CommitmentStatus.Declined;
            c.ResolvedAtUtc = now;
            c.Resolution = "You didn't accept her terms.";
        }
        await db.SaveChangesAsync(ct);
        host.Speak(Utterance.Of(accept ? SpeechAct.PromiseAccepted : SpeechAct.PromiseDeclined).With("promise", c.Terms.Describe()));
    }

    // ── Session start and end ──

    /// <summary>
    /// What this session owes: commitments in force are bound to it, and owed ledger entries are
    /// marked as being served. Debug sessions never carry obligations.
    /// </summary>
    public static async Task<SessionObligations> FoldAsync(GuidedData db, int sessionId, DateTime now, CancellationToken ct)
    {
        var commitments = await db.Commitments.Where(x => x.Status == CommitmentStatus.Active
            && ((x.Scope == CommitmentScope.Session && x.SessionId == null)
                || (x.Scope == CommitmentScope.Days && x.StartsAtUtc <= now && x.EndsAtUtc > now))).ToListAsync(ct);
        var character = new SessionCharacter();
        DateTime? until = null;
        string? reason = null;
        void Deny(DateTime end, string why)
        {
            if (until is DateTime current && current >= end) return;
            until = end;
            reason = why;
        }
        foreach (var c in commitments)
        {
            if (c.Scope == CommitmentScope.Session) c.SessionId = sessionId;
            character = character.With(c.Terms.Character);
            if (!c.NoOrgasm) continue;
            Deny(c.Scope == CommitmentScope.Session ? DateTime.MaxValue : c.EndsAtUtc!.Value, nameof(Block.Promise));
        }

        var owed = await db.Ledger.Where(x => x.Status == LedgerStatus.Owed || x.Status == LedgerStatus.Serving).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
        foreach (var e in owed.Where(x => x.Kind == LedgerKind.Denial && x.EndsAtUtc > now))
            Deny(e.EndsAtUtc!.Value, nameof(Block.Denial));
        var serving = new List<LedgerEntry>();
        var session = owed.FirstOrDefault(x => x.Kind == LedgerKind.PunishmentSession && x.Remaining > 0);
        if (session is not null) serving.Add(session);
        var ending = owed.FirstOrDefault(x => x.Kind == LedgerKind.PunishmentEnding && x.Remaining > 0);
        if (ending is not null) serving.Add(ending);
        var edges = owed.Where(x => x.Kind == LedgerKind.Edges && x.Remaining > 0).ToList();
        var ruins = owed.Where(x => x.Kind == LedgerKind.Ruin && x.Remaining > 0).ToList();
        serving.AddRange(edges);
        serving.AddRange(ruins);
        foreach (var e in serving)
        {
            e.Status = LedgerStatus.Serving;
            e.ServingSessionId = sessionId;
        }
        await db.SaveChangesAsync(ct);
        return new()
        {
            Commitments = commitments.Select(x => x.Id).ToArray(), LedgerEntries = serving.Select(x => x.Id).ToArray(), Character = character,
            NoOrgasmUntilUtc = until, NoOrgasmReason = reason, OwedEdges = edges.Sum(x => x.Remaining), OwedRuins = ruins.Sum(x => x.Remaining),
            PunishmentEnding = ending is not null, PunishmentSession = session is not null,
        };
    }

    /// <summary>A session contract with its obligations applied: character raises preferences, an owed punishment session makes it one.</summary>
    public static SessionContract Apply(SessionContract contract, SessionObligations owed)
    {
        var p = contract.Preferences;
        var c = owed.Character;
        return contract with
        {
            Obligations = owed,
            Punishment = contract.Punishment || owed.PunishmentSession,
            Preferences = p with
            {
                Length = c.Long ? Math.Max(p.Length, 8) : p.Length,
                Intensity = c.Intense ? Math.Max(p.Intensity, 8) : p.Intensity,
                Mean = c.Mean ? Math.Max(p.Mean, 8) : p.Mean,
            },
        };
    }

    /// <summary>
    /// Records what a finished session served. A session she brought to her own ending serves an
    /// owed punishment session and keeps its session promises; anything unserved is owed again, and
    /// session promises carry over to the next session.
    /// </summary>
    public static async Task SettleAsync(GuidedData db, SessionState s, DateTime now, CancellationToken ct)
    {
        if (s.Contract.Debug) return;
        bool clean = s.FinishDecided;
        var rows = await db.Ledger.Where(x => x.Status != LedgerStatus.Served
            && (x.ServingSessionId == s.SessionId || x.Kind == LedgerKind.Edges || x.Kind == LedgerKind.Ruin)).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct);
        void Serve(LedgerEntry e, int amount)
        {
            e.Remaining = Math.Max(0, e.Remaining - amount);
            if (e.Remaining == 0)
            {
                e.Status = LedgerStatus.Served;
                e.ServingSessionId = s.SessionId;
                e.ServedAtUtc = now;
            }
        }
        foreach (var e in rows.Where(x => x.Kind == LedgerKind.PunishmentSession && x.ServingSessionId == s.SessionId))
            if (clean && s.Contract.Obligations.PunishmentSession) Serve(e, 1);
        foreach (var e in rows.Where(x => x.Kind == LedgerKind.PunishmentEnding && x.ServingSessionId == s.SessionId))
            if (s.EndingsServed > 0) Serve(e, s.EndingsServed);
        int paid = s.EdgesPaid;
        foreach (var e in rows.Where(x => x.Kind == LedgerKind.Edges && paid > 0))
        {
            int take = Math.Min(paid, e.Remaining);
            Serve(e, take);
            paid -= take;
        }
        int ruined = s.RuinsServed;
        foreach (var e in rows.Where(x => x.Kind == LedgerKind.Ruin && ruined > 0))
        {
            int take = Math.Min(ruined, e.Remaining);
            Serve(e, take);
            ruined -= take;
        }
        foreach (var e in rows.Where(x => x.Status == LedgerStatus.Serving))
        {
            e.Status = LedgerStatus.Owed;
            e.ServingSessionId = null;
        }
        foreach (var c in await db.Commitments.Where(x => x.Status == CommitmentStatus.Active && x.SessionId == s.SessionId).ToListAsync(ct))
        {
            if (clean)
            {
                c.Status = CommitmentStatus.Kept;
                c.ResolvedAtUtc = now;
                c.Resolution = "The session ran until she ended it.";
            }
            else c.SessionId = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task RecordAsync(GuidedData db, int sessionId, LedgerOwed owed, DateTime now, CancellationToken ct)
    {
        db.Add(Entry(owed.Stake, owed.Origin, owed.Reason, now, sessionId, offerId: owed.OfferId));
        await db.SaveChangesAsync(ct);
    }

    private static LedgerEntry Entry(Stake stake, LedgerOrigin origin, string reason, DateTime now, int? sessionId = null,
        Guid? commitmentId = null, Guid? offerId = null, Guid? followUpId = null) => new()
    {
        Kind = stake.Kind, Amount = Math.Max(1, stake.Amount), Remaining = Math.Max(1, stake.Amount), Severity = Math.Clamp(stake.Severity, 1, 10),
        Origin = origin, Reason = reason.Length > 1000 ? reason[..1000] : reason, SessionId = sessionId, CommitmentId = commitmentId, OfferId = offerId,
        FollowUpId = followUpId, CreatedAtUtc = now, Status = LedgerStatus.Owed,
        StartsAtUtc = stake.Kind == LedgerKind.Denial ? now : null, EndsAtUtc = stake.Kind == LedgerKind.Denial ? now.AddDays(stake.Amount) : null,
    };

    // ── Reconciliation: facts first, whenever the account is seen ──

    /// <summary>
    /// Brings the ledger up to date with what happened: denial windows that ran out are served,
    /// promises whose time passed cleanly are kept, orgasms that broke a promise or a session become
    /// questions, and a session that stopped with nobody there is asked about.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken ct, bool force = false)
    {
        var now = Now;
        // Status polls arrive every second; the facts rarely change that fast.
        if (!force && now - lastReconciledUtc < ReconcileEvery) return;
        lastReconciledUtc = now;
        await using var db = data.Open();
        var scanFrom = await db.Database.SqlQuery<DateTime>($"""
            INSERT INTO aidomme_ledger_state ("UserId", "ScanFromUtc") VALUES ({db.UserId}, {now})
            ON CONFLICT ("UserId") DO UPDATE SET "ScanFromUtc" = aidomme_ledger_state."ScanFromUtc"
            RETURNING "ScanFromUtc" AS "Value"
            """).ToListAsync(ct);
        await ScanOrgasmsAsync(db, scanFrom.Single(), now, ct);

        // Open questions about orgasms that were undone in the tracker no longer stand.
        var voided = await db.FollowUps.Where(f => f.Status == FollowUpStatus.Open && f.EventId != null
            && db.Db.Set<TrackerEvent>().Any(e => e.Id == f.EventId && e.VoidedAtUtc != null)).ToListAsync(ct);
        foreach (var f in voided) f.Status = FollowUpStatus.Voided;

        foreach (var e in await db.Ledger.Where(x => x.Kind == LedgerKind.Denial && x.Status != LedgerStatus.Served && x.EndsAtUtc <= now).ToListAsync(ct))
        {
            e.Status = LedgerStatus.Served;
            e.Remaining = 0;
            e.ServedAtUtc = e.EndsAtUtc;
        }
        var open = await db.FollowUps.Where(x => x.Status == FollowUpStatus.Open).Select(x => x.Commitments).ToListAsync(ct);
        var questioned = open.SelectMany(x => JsonSerializer.Deserialize<Guid[]>(x, Json) ?? []).ToHashSet();
        foreach (var c in await db.Commitments.Where(x => x.Status == CommitmentStatus.Active && x.Scope == CommitmentScope.Days && x.EndsAtUtc <= now).ToListAsync(ct))
        {
            if (questioned.Contains(c.Id)) continue;
            c.Status = CommitmentStatus.Kept;
            c.ResolvedAtUtc = c.EndsAtUtc;
            c.Resolution = "No breach was observed.";
        }

        // Anything left serving or bound to a session that is no longer running goes back to owed.
        int? active = host.ActiveSessionId;
        foreach (var e in await db.Ledger.Where(x => x.Status == LedgerStatus.Serving && x.ServingSessionId != active).ToListAsync(ct))
        {
            e.Status = LedgerStatus.Owed;
            e.ServingSessionId = null;
        }
        foreach (var c in await db.Commitments.Where(x => x.Status == CommitmentStatus.Active && x.Scope == CommitmentScope.Session
                     && x.SessionId != null && x.SessionId != active).ToListAsync(ct))
            c.SessionId = null;
        await db.SaveChangesAsync(ct);

        await AskAboutInterruptionAsync(db, now, ct);
        // Events received from here on are looked at next time; a small overlap covers slow commits.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE aidomme_ledger_state SET "ScanFromUtc" = GREATEST("ScanFromUtc", {now.AddMinutes(-2)}) WHERE "UserId" = {db.UserId}
            """, ct);
    }

    private async Task ScanOrgasmsAsync(GuidedData db, DateTime scanFrom, DateTime now, CancellationToken ct)
    {
        var events = await db.Db.Set<TrackerEvent>().AsNoTracking()
            .Where(e => e.UserId == db.UserId && e.Kind == "orgasm" && e.VoidedAtUtc == null && e.ReceivedAtUtc >= scanFrom)
            .Where(e => !db.FollowUps.Any(f => f.EventId == e.Id))
            .OrderBy(e => e.OccurredAtUtc).ToListAsync(ct);
        if (events.Count == 0) return;
        var ids = events.Select(x => x.Id).ToList();
        var receipts = await db.TrackerReceipts.AsNoTracking().Where(x => ids.Contains(x.EventId))
            .Join(db.Orgasms, r => r.ActivityId, o => o.Id, (r, o) => new { r.EventId, r.SessionId, o.Allowed })
            .Join(db.Sessions, x => x.SessionId, s => s.Id, (x, s) => new { x.EventId, x.SessionId, x.Allowed, s.IsDebugSession })
            .ToListAsync(ct);
        var commitments = await db.Commitments.AsNoTracking()
            .Where(x => x.NoOrgasm && (x.Status == CommitmentStatus.Active || x.Status == CommitmentStatus.Kept) && x.StartsAtUtc != null).ToListAsync(ct);
        var sessionStarts = await db.Sessions.AsNoTracking().Where(x => commitments.Select(c => c.SessionId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.StartedAtUtc, ct);
        var denials = await db.Ledger.AsNoTracking().Where(x => x.Kind == LedgerKind.Denial && x.StartsAtUtc != null).ToListAsync(ct);
        var state = host.State;

        foreach (var e in events)
        {
            var receipt = receipts.FirstOrDefault(x => x.EventId == e.Id);
            if (receipt is { Allowed: true } or { IsDebugSession: true }) continue;
            int? session = receipt?.SessionId;
            // Recorded while her session was paused or not delivered: still in that session.
            if (session is null && state is { Active: true, PermissionGranted: false } s && !s.Contract.Debug && s.StartedAtUtc <= e.OccurredAtUtc)
                session = s.SessionId;
            var broken = commitments.Where(c => Covers(c, e.OccurredAtUtc, now, sessionStarts)).Select(c => c.Id).ToArray();
            bool duringDenial = denials.Any(d => d.StartsAtUtc <= e.OccurredAtUtc && d.EndsAtUtc > e.OccurredAtUtc);
            if (session is null && broken.Length == 0 && !duringDenial) continue;
            var promised = broken.Length > 0 ? " while you had promised me no orgasm" : duringDenial ? " during your denial" : "";
            db.Add(new FollowUpRecord
            {
                Id = Guid.NewGuid(), Kind = FollowUpKind.UnauthorizedOrgasm, Status = FollowUpStatus.Open, SessionId = session, EventId = e.Id,
                OccurredAtUtc = e.OccurredAtUtc, Commitments = JsonSerializer.Serialize(broken, Json), DuringDenial = duringDenial, CreatedAtUtc = now,
                Observed = session is int id ? $"An orgasm was recorded during session {id} without permission{promised}." : $"An orgasm was recorded{promised}.",
            });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Whether a no-orgasm promise was in force at <paramref name="at"/>.</summary>
    private static bool Covers(CommitmentRecord c, DateTime at, DateTime now, IReadOnlyDictionary<int, DateTime> sessionStarts)
    {
        if (c.Scope == CommitmentScope.Days) return c.StartsAtUtc <= at && c.EndsAtUtc > at;
        return c.SessionId is int session && sessionStarts.TryGetValue(session, out var started) && started <= at && (c.ResolvedAtUtc ?? now) >= at;
    }

    private async Task AskAboutInterruptionAsync(GuidedData db, DateTime now, CancellationToken ct)
    {
        await host.InterruptLongPauseAsync(AbandonedPauseAfter, ct);
        var s = host.State;
        if (s is not { Active: true, InterruptedAtUtc: DateTime at } || s.Contract.Debug) return;
        if (await db.FollowUps.AnyAsync(x => x.Kind == FollowUpKind.InterruptedSession && x.SessionId == s.SessionId && x.Status == FollowUpStatus.Open, ct)) return;
        var orgasms = await db.Db.Set<TrackerEvent>().CountAsync(e => e.UserId == db.UserId && e.Kind == "orgasm" && e.VoidedAtUtc == null && e.OccurredAtUtc >= at, ct);
        db.Add(new FollowUpRecord
        {
            Id = Guid.NewGuid(), Kind = FollowUpKind.InterruptedSession, Status = FollowUpStatus.Open, SessionId = s.SessionId, OccurredAtUtc = at,
            Commitments = JsonSerializer.Serialize(s.Contract.Obligations.Commitments, Json), CreatedAtUtc = now,
            Observed = $"Session {s.SessionId} stopped with nobody here."
                + (orgasms > 0 ? $" The tracker recorded {Stake.Plural(orgasms, "orgasm")} since then." : " Nothing was recorded since then."),
        });
        await db.SaveChangesAsync(ct);
    }

    // ── Answers ──

    public async Task AnswerAsync(Guid id, string answer, CancellationToken ct)
    {
        if (!host.Active) await host.PrepareIdleAsync(ct);
        await using var db = data.Open();
        var f = await db.FollowUps.SingleOrDefaultAsync(x => x.Id == id && x.Status == FollowUpStatus.Open, ct)
            ?? throw new InvalidOperationException("That question has already been answered.");
        answer = (answer ?? "").Trim().ToLowerInvariant();
        if (!View(f).Answers.Any(x => x.Value == answer)) throw new ArgumentException("That isn't one of the answers.");
        var now = Now;
        int mean = (await host.ResolvedDefaultsAsync(ct)).Mean;
        f.Status = FollowUpStatus.Answered;
        f.Answer = answer;
        f.AnsweredAtUtc = now;
        var lines = new List<Utterance>();
        SessionOutcome? end = null;
        bool resume = false;

        if (f.Kind == FollowUpKind.UnauthorizedOrgasm)
            lines.Add(await OrgasmAsync(db, f, answer == "intent", mean, now, ct));
        else
        {
            switch (answer)
            {
                case "interrupted":
                    if (now - f.OccurredAtUtc > ResumableFor)
                    {
                        end = SessionOutcome.Interrupted;
                        lines.Add(Utterance.Of(SpeechAct.FollowUpAnswer, "interrupted", "too-late"));
                    }
                    else
                    {
                        resume = true;
                        lines.Add(Utterance.Of(SpeechAct.FollowUpAnswer, "interrupted", "resume"));
                    }
                    break;
                case "quit":
                {
                    end = SessionOutcome.Abandoned;
                    var stakes = BreachRules.ForQuit(mean);
                    var broken = await BreakAsync(db, f, CommitmentsOf(f), c => c.Scope == CommitmentScope.Session || c.HasCharacter(),
                        "You walked out of a session you promised her.", intentional: true, now, ct);
                    foreach (var stake in stakes)
                        db.Add(Entry(stake, LedgerOrigin.Rule, $"You walked out of session {f.SessionId}.", now, f.SessionId, followUpId: f.Id));
                    lines.Add(Utterance.Of(SpeechAct.FollowUpAnswer, "quit", broken.Count > 0 ? "broke-promise" : "no-promise")
                        .With("stakes", Stake.Describe(Stake.Merge(stakes.Concat(broken)))));
                    break;
                }
                default:
                {
                    end = SessionOutcome.UnauthorizedOrgasm;
                    bool intentional = answer == "came-intent";
                    var recorded = await db.FollowUps.Where(x => x.Kind == FollowUpKind.UnauthorizedOrgasm && x.Status == FollowUpStatus.Open
                        && x.SessionId == f.SessionId && x.OccurredAtUtc >= f.OccurredAtUtc).ToListAsync(ct);
                    if (recorded.Count > 0)
                        foreach (var r in recorded)
                        {
                            r.Status = FollowUpStatus.Answered;
                            r.Answer = intentional ? "intent" : "accident";
                            r.AnsweredAtUtc = now;
                            lines.Add(await OrgasmAsync(db, r, intentional, mean, now, ct));
                        }
                    else
                    {
                        f.DuringDenial = await db.Ledger.AnyAsync(x => x.Kind == LedgerKind.Denial && x.StartsAtUtc <= f.OccurredAtUtc && x.EndsAtUtc > f.OccurredAtUtc, ct);
                        lines.Add(await OrgasmAsync(db, f, intentional, mean, now, ct));
                    }
                    break;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var line in lines) host.Speak(line);
        if (f.Kind == FollowUpKind.InterruptedSession && host.ActiveSessionId == f.SessionId)
        {
            if (end is SessionOutcome outcome) await host.EndAsync(outcome, ct);
            else if (resume) host.ClearInterruption();
        }
    }

    private static Guid[] CommitmentsOf(FollowUpRecord f) => JsonSerializer.Deserialize<Guid[]>(f.Commitments, Json) ?? [];

    /// <summary>The consequences of an orgasm without permission: a broken promise's stakes, or the standing rules.</summary>
    private static async Task<Utterance> OrgasmAsync(GuidedData db, FollowUpRecord f, bool intentional, int mean, DateTime now, CancellationToken ct)
    {
        var how = intentional ? "on purpose, by your own account" : "an accident, by your own account";
        var broken = await BreakAsync(db, f, CommitmentsOf(f), c => c.NoOrgasm, $"An orgasm without permission ({how}).", intentional, now, ct);
        var stakes = broken;
        if (broken.Count == 0)
        {
            stakes = BreachRules.ForOrgasm(intentional, mean, f.DuringDenial);
            foreach (var stake in stakes)
                db.Add(Entry(stake, LedgerOrigin.Rule, $"You came without permission ({how}).", now, f.SessionId, followUpId: f.Id));
        }
        return Utterance.Of(SpeechAct.FollowUpAnswer, "orgasm", intentional ? "intent" : "accident", broken.Count > 0 ? "broke-promise" : "no-promise")
            .With("stakes", Stake.Describe(stakes));
    }

    /// <summary>Marks the promises this breaks as broken and writes their stakes to the ledger.</summary>
    private static async Task<List<Stake>> BreakAsync(GuidedData db, FollowUpRecord f, Guid[] ids, Func<CommitmentRecord, bool> applies,
        string resolution, bool intentional, DateTime now, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var all = new List<Stake>();
        foreach (var c in await db.Commitments.Where(x => ids.Contains(x.Id) && (x.Status == CommitmentStatus.Active || x.Status == CommitmentStatus.Kept)).ToListAsync(ct))
        {
            if (!applies(c)) continue;
            c.Status = CommitmentStatus.Broken;
            c.ResolvedAtUtc = now;
            c.Resolution = resolution;
            var stakes = BreachRules.ForCommitment(StakesOf(c), intentional);
            foreach (var stake in stakes)
                db.Add(Entry(stake, LedgerOrigin.Commitment, $"You broke your promise: {c.Terms.Describe()}.", now, f.SessionId, commitmentId: c.Id, followUpId: f.Id));
            all.AddRange(stakes);
        }
        return Stake.Merge(all);
    }

    private static string Truncate(string? value, int length) => value is null ? "" : value.Length <= length ? value : value[..length];
}

internal static class CommitmentRecordExtensions
{
    public static bool HasCharacter(this CommitmentRecord c) => c.Long || c.Intense || c.Torturous || c.Mean || c.Teasing;
}
