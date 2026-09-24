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
  learnedPerMinute: number;
  currentPerEdge: number | null;
  learnedPerEdge: number | null;
  comparisons: number;
};
type Lab = {
  mode: "Stim" | "Ender";
  comparisons: number;
  yourComparisons: number;
  confidence: number;
  left: Side | null;
  right: Side | null;
  methods: MethodRow[];
  enders: Candidate[];
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

function Change({ now, learned }: { now: number; learned: number }) {
  const diff = learned - now;
  const pct = now > 0 ? Math.round((diff / now) * 100) : 0;
  if (Math.abs(pct) < 5) return <span className="text-muted">about the same</span>;
  return <span className={diff > 0 ? "text-amber-300" : "text-sky-300"}>{pct > 0 ? `+${pct}%` : `${pct}%`}</span>;
}

/** One side of the matchup: what he would be told to do, and the buttons to choose it. */
function Card({ side, onChoose, busy }: { side: Side; onChoose: (strength: 1 | 2 | 3) => void; busy: boolean }) {
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
      <div className="mt-auto text-xs font-medium uppercase tracking-wide text-muted">I'd rather do this one</div>
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
  const [mode, setMode] = useState<"Stim" | "Ender">("Stim");
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
      if (next.methods.length) setMethodNames(Object.fromEntries(next.methods.map((m) => [String(m.id), m.name])));
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    void run(() => request(`/lab?mode=${mode}`));
  }, [mode, run]);

  const choose = (winner: Side, loser: Side, strength: 1 | 2 | 3) =>
    void run(() =>
      request("/comparisons", "POST", { mode, winnerKey: winner.candidate.key, loserKey: loser.candidate.key, strength }),
    );

  const optimize = async () => {
    setBusy(true);
    setError("");
    try {
      setOptimized(await request("/optimize", "POST"));
      setLab(await request(`/lab?mode=${mode}`));
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
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              aria-pressed={mode === key}
              className={`rounded-md px-3 py-1.5 ${mode === key ? "bg-accent text-white" : "text-secondary hover:text-foreground"}`}
              onClick={() => {
                setMode(key);
                setOptimized(null);
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
        <p className="text-base font-medium text-foreground">Which would you rather be told to do?</p>
        <p className="mt-1 text-sm text-secondary">
          Pick the easier one for you, and how strongly. Every answer teaches the lab how hard each{" "}
          {mode === "Stim" ? "method, time and number of edges" : "punishment ending"} really is for you.
        </p>
      </div>

      {lab?.left && lab.right ? (
        <div className="grid gap-4 md:grid-cols-2">
          <Card side={lab.left} busy={busy} onChoose={(s) => choose(lab.left!, lab.right!, s)} />
          <Card side={lab.right} busy={busy} onChoose={(s) => choose(lab.right!, lab.left!, s)} />
        </div>
      ) : (
        <p className="rounded-2xl border border-dashed border-border px-4 py-6 text-sm text-secondary">
          {lab ? "Nothing to compare yet." : "Loading…"}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <button type="button" className={secondaryBtn} disabled={busy} onClick={() => void run(() => request(`/lab?mode=${mode}`))}>
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

      {lab && mode === "Stim" && (
        <Section
          title="What your methods are worth"
          hint="Now: what a method earns in a session today. Suggested: what your answers so far point to. Apply sets your methods from all your answers."
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
                {[...lab.methods]
                  .sort((a, b) => b.learnedPerMinute - a.learnedPerMinute)
                  .map((m) => (
                    <tr key={m.id} className="border-t border-border bg-card">
                      <td className="px-3 py-2 text-foreground">{m.name}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-secondary">{m.currentPerMinute.toFixed(1)}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-foreground">
                        {m.learnedPerMinute.toFixed(1)} <Change now={m.currentPerMinute} learned={m.learnedPerMinute} />
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums text-secondary">{m.currentPerEdge?.toFixed(1) ?? "n/a"}</td>
                      <td className="px-3 py-2 text-right tabular-nums text-foreground">
                        {m.learnedPerEdge != null && m.currentPerEdge != null ? (
                          <>
                            {m.learnedPerEdge.toFixed(1)} <Change now={m.currentPerEdge} learned={m.learnedPerEdge} />
                          </>
                        ) : (
                          "n/a"
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
