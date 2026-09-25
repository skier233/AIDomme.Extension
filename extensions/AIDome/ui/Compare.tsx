import React, { useCallback, useEffect, useState } from "react";
import { request } from "./Configuration";
import { Notice, Section, primaryBtn, secondaryBtn } from "./Ui";

type Candidate = {
  key: string;
  group: string;
  title: string;
  description: string;
  meta: string[];
  points: number;
  wins: number;
  losses: number;
};
type Side = { candidate: Candidate; harderChance: number };
type MethodRow = {
  id: number;
  name: string;
  currentPerMinute: number;
  suggestedPerMinute: number | null;
  currentPerEdge: number | null;
  suggestedPerEdge: number | null;
  comparisons: number;
};
type DealRow = {
  id: number;
  name: string;
  kind: string;
  currentLow: number;
  currentHigh: number;
  suggestedLow: number;
  suggestedHigh: number;
  factor: number;
  comparisons: number;
};
type Mode = "Stim" | "Ender" | "Offer";
type Lab = {
  mode: Mode;
  comparisons: number;
  yourComparisons: number;
  confidence: number;
  left: Side | null;
  right: Side | null;
  methods: MethodRow[];
  enders: Candidate[];
  deals?: DealRow[];
  optimizeNeeds: number;
  pointsPerMinute: number;
};
type Optimized = {
  winningAlgorithm: string;
  validationAccuracy: number;
  multipliers: Record<string, { timePointsPerSecond: number; edgePointsPerEdge: number }>;
  before: Record<string, { timePointsPerSecond: number; edgePointsPerEdge: number }>;
};

const strengths: [1 | 2 | 3, string][] = [
  [1, "Slightly"],
  [2, "Rather"],
  [3, "Much rather"],
];

function Change({ now, learned }: { now: number; learned: number | null }) {
  if (learned == null) return null;
  const diff = learned - now;
  const pct = now > 0 ? Math.round((diff / now) * 100) : 0;
  if (Math.abs(pct) < 5) return <span className="text-muted">about the same</span>;
  return <span className={diff > 0 ? "text-amber-300" : "text-sky-300"}>{pct > 0 ? `+${pct}%` : `${pct}%`}</span>;
}

/** One side of the matchup: what he would be told to do, and the buttons to choose it. */
function Card({ side, onChoose, busy, verb }: { side: Side; onChoose: (strength: 1 | 2 | 3) => void; busy: boolean; verb: string }) {
  const c = side.candidate;
  return (
    <div className="flex flex-col gap-4 rounded-2xl border border-border bg-card p-5">
      <div className="flex items-center justify-between gap-2">
        <span className="rounded-full bg-surface px-2.5 py-1 text-xs font-medium text-secondary">{c.group}</span>
        <span className="text-xs text-muted" title="The lab's current estimate">
          ≈ {c.points.toFixed(c.points < 10 ? 1 : 0)} {c.group === "Ending" ? "rating" : "points"}
        </span>
      </div>
      <div>
        <h3 className="text-lg font-semibold text-foreground">{c.title}</h3>
        {c.description && <p className="mt-1 text-sm text-secondary">{c.description}</p>}
      </div>
      <div className="flex flex-wrap gap-1.5">
        {c.meta.map((m) => (
          <span key={m} className="rounded-md border border-border bg-surface px-2 py-0.5 text-xs text-secondary">
            {m}
          </span>
        ))}
      </div>
      <p className="text-xs text-muted">
        The lab thinks this is the {side.harderChance >= 0.5 ? "harder" : "easier"} one ({Math.round(Math.max(side.harderChance, 1 - side.harderChance) * 100)}%).
      </p>
      <div className="mt-auto text-xs font-medium uppercase tracking-wide text-muted">I'd rather {verb} this one</div>
      <div className="grid grid-cols-3 gap-2">
        {strengths.map(([strength, label]) => (
          <button
            key={strength}
            type="button"
            disabled={busy}
            className={strength === 2 ? primaryBtn + " justify-center" : secondaryBtn + " justify-center"}
            aria-label={`${label}: ${c.title}`}
            onClick={() => onChoose(strength)}
          >
            {label}
          </button>
        ))}
      </div>
    </div>
  );
}

/**
 * Side-by-side calibration, as in the original app: which of two assignments would he rather be
 * given? Every answer refits the lab's model; the table shows what each method earns now and what
 * the answers suggest, and "Apply" runs the optimizer to set his methods' points from them.
 */
export function CompareLab() {
  const [mode, setMode] = useState<Mode>("Stim");
  const [suggested, setSuggested] = useState<MethodRow[] | null>(null);
  const [notice, setNotice] = useState("");
  const loadId = React.useRef(0);
  // The pairs shown lately (newest last): the server never offers the same one straight back, and
  // avoids the same two methods, endings or deals again so soon.
  const shown = React.useRef<string[]>([]);
  const labUrl = (m: Mode) => `/lab?mode=${m}&recent=${encodeURIComponent(shown.current.join(","))}`;
  const [lab, setLab] = useState<Lab | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [optimized, setOptimized] = useState<Optimized | null>(null);
  const [methodNames, setMethodNames] = useState<Record<string, string>>({});

  const run = useCallback(async (operation: () => Promise<Lab>) => {
    setBusy(true);
    setError("");
    try {
      const next = await operation();
      setLab(next);
      if (next.left && next.right) shown.current = [...shown.current, `${next.left.candidate.key}|${next.right.candidate.key}`].slice(-8);
      // What Apply would set takes a moment to work out: it arrives after the next matchup.
      if (next.mode === "Stim") {
        const id = ++loadId.current;
        setSuggested(null);
        request("/lab/suggestions")
          .then((rows: MethodRow[]) => id === loadId.current && setSuggested(rows))
          .catch(() => undefined);
      }
      if (next.methods.length) setMethodNames(Object.fromEntries(next.methods.map((m) => [String(m.id), m.name])));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void run(() => request(labUrl(mode)));
  }, [mode, run]);

  const choose = (winner: Side, loser: Side, strength: 1 | 2 | 3) =>
    void run(() =>
      request("/comparisons", "POST", {
        mode,
        winnerKey: winner.candidate.key,
        loserKey: loser.candidate.key,
        strength,
        recent: shown.current.join(","),
      }),
    );

  const applyDeals = async () => {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = await request("/optimize-deals", "POST");
      setNotice(`Repriced ${result.deals.length} deal${result.deals.length === 1 ? "" : "s"} from your answers.`);
      setLab(await request(labUrl(mode)));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const optimize = async () => {
    setBusy(true);
    setError("");
    try {
      setOptimized(await request("/optimize", "POST"));
      setLab(await request(labUrl(mode)));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex rounded-lg border border-border bg-surface p-0.5 text-sm" role="group" aria-label="What to compare">
          {(
            [
              ["Stim", "Methods"],
              ["Ender", "Punishment endings"],
              ["Offer", "Deals"],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              aria-pressed={mode === key}
              className={`rounded-md px-3 py-1.5 ${mode === key ? "bg-accent text-white" : "text-secondary hover:text-foreground"}`}
              onClick={() => {
                shown.current = [];
                setMode(key);
                setOptimized(null);
                setNotice("");
              }}
            >
              {label}
            </button>
          ))}
        </div>
        {lab && (
          <div className="flex flex-wrap gap-2 text-xs">
            <span className="rounded-full border border-border bg-card px-3 py-1 text-secondary">
              {lab.comparisons} comparisons ({lab.yourComparisons} from this account)
            </span>
            <span className="rounded-full border border-border bg-card px-3 py-1 text-secondary">
              Model confidence {Math.round(lab.confidence * 100)}%
            </span>
            {mode === "Stim" && (
              <span className="rounded-full border border-border bg-card px-3 py-1 text-secondary">
                Your pace ≈ {lab.pointsPerMinute.toFixed(1)} points a minute
              </span>
            )}
          </div>
        )}
      </div>

      <div className="rounded-2xl border border-accent/30 bg-accent/10 px-5 py-4">
        <p className="text-base font-medium text-foreground">
          {mode === "Offer" ? "Which price would you rather pay?" : "Which would you rather be told to do?"}
        </p>
        <p className="mt-1 text-sm text-secondary">
          Pick the easier one for you, and how strongly. Every answer teaches the lab how hard each{" "}
          {mode === "Stim" ? "method, time and number of edges" : mode === "Ender" ? "punishment ending" : "deal"} really is for you.
          {mode === "Offer" && " Some matchups set a deal against a method, so deals are priced on the same scale as everything else."}
        </p>
      </div>

      {lab?.left && lab.right ? (
        <div className="grid gap-4 md:grid-cols-2">
          <Card side={lab.left} busy={busy} verb={mode === "Offer" ? "take" : "do"} onChoose={(s) => choose(lab.left!, lab.right!, s)} />
          <Card side={lab.right} busy={busy} verb={mode === "Offer" ? "take" : "do"} onChoose={(s) => choose(lab.right!, lab.left!, s)} />
        </div>
      ) : (
        <p className="rounded-2xl border border-dashed border-border px-4 py-6 text-sm text-secondary">
          {lab ? "Nothing to compare yet." : "Loading…"}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button type="button" className={secondaryBtn} disabled={busy} onClick={() => void run(() => request(labUrl(mode)))}>
          Skip this one
        </button>
        <button
          type="button"
          className={secondaryBtn}
          disabled={busy || !lab?.yourComparisons}
          onClick={() => void run(() => request(`/comparisons/last?mode=${mode}`, "DELETE"))}
        >
          Undo my last answer
        </button>
      </div>
      {error && <Notice tone="error">{error}</Notice>}
      {notice && <Notice tone="ok">{notice}</Notice>}

      {lab && mode === "Stim" && (
        <Section
          title="What your methods are worth"
          hint="Now: what a method earns in a session today. Suggested: exactly what Apply would set, from all your answers so far (it updates a moment after each answer)."
          aside={
            <button type="button" className={primaryBtn} disabled={busy || lab.optimizeNeeds > 0} onClick={() => void optimize()}>
              {lab.optimizeNeeds > 0 ? `${lab.optimizeNeeds} more answers to apply` : "Apply to my methods"}
            </button>
          }
        >
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full text-sm">
              <thead className="bg-card/60 text-left text-xs font-medium uppercase tracking-wide text-muted">
                <tr>
                  <th className="px-3 py-2">Method</th>
                  <th className="px-3 py-2 text-right">Per minute now</th>
                  <th className="px-3 py-2 text-right">Suggested</th>
                  <th className="px-3 py-2 text-right">Per edge now</th>
                  <th className="px-3 py-2 text-right">Suggested</th>
                  <th className="px-3 py-2 text-right">Answers</th>
                </tr>
              </thead>
              <tbody>
                {[...(suggested ?? lab.methods)]
                  .sort((a, b) => (b.suggestedPerMinute ?? b.currentPerMinute) - (a.suggestedPerMinute ?? a.currentPerMinute))
                  .map((m) => (
                    <tr key={m.id} className="border-t border-border bg-card">
                      <td className="px-3 py-2 text-foreground">{m.name}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-secondary">{m.currentPerMinute.toFixed(1)}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-foreground">
                        {m.suggestedPerMinute != null ? (
                          <>
                            {m.suggestedPerMinute.toFixed(1)} <Change now={m.currentPerMinute} learned={m.suggestedPerMinute} />
                          </>
                        ) : (
                          <span className="text-muted">{suggested ? "n/a" : "…"}</span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums text-secondary">{m.currentPerEdge?.toFixed(1) ?? "n/a"}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-foreground">
                        {m.suggestedPerEdge != null && m.currentPerEdge != null ? (
                          <>
                            {m.suggestedPerEdge.toFixed(1)} <Change now={m.currentPerEdge} learned={m.suggestedPerEdge} />
                          </>
                        ) : (
                          <span className="text-muted">{m.currentPerEdge == null || suggested ? "n/a" : "…"}</span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums text-secondary">{m.comparisons}</td>
                    </tr>
                  ))}
              </tbody>
            </table>
          </div>
        </Section>
      )}

      {optimized && (
        <Section
          title="Applied"
          hint={`${optimized.winningAlgorithm} predicted ${Math.round(optimized.validationAccuracy * 100)}% of held-back answers. Your methods now earn:`}
        >
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full text-sm">
              <thead className="bg-card/60 text-left text-xs font-medium uppercase tracking-wide text-muted">
                <tr>
                  <th className="px-3 py-2">Method</th>
                  <th className="px-3 py-2 text-right">Per minute</th>
                  <th className="px-3 py-2 text-right">Per edge</th>
                </tr>
              </thead>
              <tbody>
                {Object.entries(optimized.multipliers).map(([id, after]) => {
                  const before = optimized.before?.[id];
                  return (
                    <tr key={id} className="border-t border-border bg-card">
                      <td className="px-3 py-2 text-foreground">{methodNames[id] ?? `Method ${id}`}</td>
                      <td className="px-3 py-2 text-right tabular-nums">
                        {before && <span className="text-muted">{(before.timePointsPerSecond * 60).toFixed(1)} → </span>}
                        {(after.timePointsPerSecond * 60).toFixed(1)}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">
                        {before && <span className="text-muted">{before.edgePointsPerEdge.toFixed(1)} → </span>}
                        {after.edgePointsPerEdge.toFixed(1)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Section>
      )}

      {lab && mode === "Offer" && lab.deals && (
        <Section
          title="What her deals are worth"
          hint="In session points, from the smallest to the largest amount she can ask. Suggested: what your answers point to. Apply reprices the deals you've compared."
          aside={
            <button type="button" className={primaryBtn} disabled={busy || !lab.deals.some((d) => d.comparisons > 0)} onClick={() => void applyDeals()}>
              Apply to my deals
            </button>
          }
        >
          <div className="overflow-x-auto rounded-xl border border-border">
            <table className="w-full text-sm">
              <thead className="bg-card/60 text-left text-xs font-medium uppercase tracking-wide text-muted">
                <tr>
                  <th className="px-3 py-2">Deal</th>
                  <th className="px-3 py-2">Price</th>
                  <th className="px-3 py-2 text-right">Worth now</th>
                  <th className="px-3 py-2 text-right">Suggested</th>
                  <th className="px-3 py-2 text-right">Answers</th>
                </tr>
              </thead>
              <tbody>
                {lab.deals.map((d) => (
                  <tr key={d.id} className="border-t border-border bg-card">
                    <td className="px-3 py-2 text-foreground">{d.name}</td>
                    <td className="px-3 py-2 text-secondary">{d.kind}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-secondary">
                      {Math.round(d.currentLow)}–{Math.round(d.currentHigh)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-foreground">
                      {d.comparisons > 0 ? (
                        <>
                          {Math.round(d.suggestedLow)}–{Math.round(d.suggestedHigh)} <Change now={d.currentHigh} learned={d.suggestedHigh} />
                        </>
                      ) : (
                        <span className="text-muted">n/a</span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-secondary">{d.comparisons}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Section>
      )}

      {lab && mode === "Ender" && lab.enders.length > 0 && (
        <Section title="How hard each ending is for you" hint="Hardest first, from your answers.">
          <ol className="space-y-1">
            {lab.enders.map((e, i) => (
              <li key={e.key} className="flex items-center justify-between rounded-lg border border-border bg-card px-3 py-2 text-sm">
                <span className="text-foreground">
                  {i + 1}. {e.title}
                </span>
                <span className="tabular-nums text-secondary">
                  {Math.round(e.points)} · {e.losses} harder, {e.wins} easier
                </span>
              </li>
            ))}
          </ol>
        </Section>
      )}
    </div>
  );
}
