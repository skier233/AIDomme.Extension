import React, { useEffect, useState } from "react";
import { extensionFetch } from "@cove/runtime/api";
import { Notice, Section, Slider, inputCls, primaryBtn, secondaryBtn, selectCls } from "./Ui";

type Definition = { id: number; name: string; [key: string]: any };
type View = {
  value: Definition;
  isOverride: boolean;
  sharedDefault?: Definition;
};
type Configuration = {
  methods: View[];
  enders: View[];
  offers: View[];
  ratings: View[];
  comparisons: View[];
};
const methodFields = [
  ["name", "Name", "text"],
  ["description", "Instructions", "textarea"],
  ["intensity", "Intensity", "scale"],
  ["tease", "Tease", "scale"],
  ["tortLevel", "Torture level", "scale"],
  ["closeness", "Closeness (how easily it gets you to the edge)", "scale"],
  ["requiresLube", "Requires lube", "checkbox"],
  ["toyCategory", "Toy", "toy"],
  ["timePointMultiplier", "Points per second", "number"],
  ["edgePointMultiplier", "Points per edge", "number"],
];
const enderFields = [
  ["name", "Name", "text"],
  ["announcementText", "Announcement", "textarea"],
  ["instructionText", "Instructions", "textarea"],
  ["severity", "Severity", "scale"],
  ["anticipation", "Anticipation", "scale"],
  ["humiliation", "Humiliation", "scale"],
  ["requiresBondage", "Requires bondage", "checkbox"],
  ["requiresCEI", "Requires CEI", "checkbox"],
  ["requiresSelfOral", "Requires self-oral", "checkbox"],
  ["endsInOrgasm", "Ends in an orgasm", "checkbox"],
];
const offerFields = [
  ["name", "Name", "text"],
  ["wording", "Her words ([amount], [hold], [s] and [what] are filled in)", "textarea"],
  ["kind", "Price", "offerKind"],
  ["minAmount", "Smallest amount", "number"],
  ["maxAmount", "Largest amount", "number"],
  ["minHold", "Shortest hold (seconds, edges now only)", "number"],
  ["maxHold", "Longest hold (seconds)", "number"],
  ["basePoints", "Base value (session points)", "number"],
  ["pointsPerUnit", "Value per unit", "number"],
  ["pointsPerHoldSecond", "Value per hold second, per edge", "number"],
  ["flavor", "Flavor", "flavor"],
  ["moods", "Moods that favour it (comma-separated)", "text"],
  ["forOrgasm", "Can buy an orgasm", "checkbox"],
  ["forRest", "Can buy a rest", "checkbox"],
  ["forMercy", "Can buy mercy after a lost game", "checkbox"],
  ["enabled", "She uses it", "checkbox"],
];
const choices: Record<string, string[]> = {
  toy: ["None", "HeadVibrator", "Fleshlight", "Autoblow", "ProstateMassager"],
  offerKind: [
    "EdgesNow",
    "EdgesLater",
    "Denial",
    "PunishmentSession",
    "PunishmentEnding",
    "Ruin",
    "RuinNow",
  ],
  flavor: ["Strict", "Cruel", "Playful", "Teasing", "Humiliating"],
};
const fields = { methods: methodFields, enders: enderFields, offers: offerFields };

export async function request(path = "", method = "GET", body?: unknown) {
  const response = await extensionFetch(
    `/api/ext/aidomme/configuration${path}`,
    {
      method,
      headers: { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    },
  );
  const text = await response.text();
  let data;
  try {
    data = JSON.parse(text);
  } catch {
    throw new Error(
      "Configuration is unavailable. Check that the updated AI Domme backend is loaded.",
    );
  }
  if (!response.ok)
    throw new Error(
      data.message || data.detail || "Could not save configuration.",
    );
  return data;
}

type Kind = "methods" | "enders" | "offers";
const kinds: [Kind, string, string][] = [
  ["methods", "Methods", "How you stroke: what she can ask of you, how intense it is, and what it earns."],
  ["enders", "Punishment endings", "How a session ends when she owes you a punishment ending."],
  ["offers", "Deals", "What she asks in return when you beg: the price, and what it is worth to her."],
];

/** Points a typical session earns per minute of play: the default the session budget starts from. */
const PointsPerMinute = 4;

/** What a new definition starts as, before the user edits it. */
const blanks: Record<Kind, Definition> = {
  methods: {
    id: 0, name: "New method", description: "", intensity: 5, tease: 0, tortLevel: 0, closeness: 5,
    requiresLube: false, toyCategory: "None", timePointMultiplier: 0, edgePointMultiplier: 0,
  },
  enders: {
    id: 0, name: "New punishment ending", announcementText: "", instructionText: "", severity: 5, anticipation: 5, humiliation: 3,
    requiresBondage: false, requiresCEI: false, requiresSelfOral: false, endsInOrgasm: true,
  },
  offers: {
    id: 0, name: "New deal", wording: "Edge [amount] more time[s] for me, and then you may [what].", kind: "EdgesNow",
    minAmount: 3, maxAmount: 10, minHold: 0, maxHold: 0, basePoints: 0, pointsPerUnit: 4, pointsPerHoldSecond: 0,
    flavor: "Strict", moods: "", forOrgasm: true, forRest: false, forMercy: false, enabled: true,
  },
};

/** Taken out of the library: methods and endings carry a flag; a deal is simply switched off. */
const isRemoved = (kind: Kind, d: Definition) => (kind === "offers" ? d.enabled === false : !!d.removed);

/** One line under a definition's name in the list: the numbers that matter when comparing them. */
function summary(kind: Kind, d: Definition): string {
  if (kind === "methods")
    return `Intensity ${d.intensity} · Torture ${d.tortLevel} · Closeness ${d.closeness}`;
  if (kind === "enders") return `Severity ${d.severity}${d.endsInOrgasm === false ? " · no orgasm" : ""}`;
  const [low, high] = valueRange(d);
  return `${d.kind} · ${Math.round(low)}–${Math.round(high)} pts${d.enabled === false ? " · off" : ""}`;
}

function valueRange(d: Definition): [number, number] {
  const per = (hold: number) => (d.pointsPerUnit ?? 0) + hold * (d.pointsPerHoldSecond ?? 0);
  return [
    (d.basePoints ?? 0) + (d.minAmount ?? 0) * per(d.minHold ?? 0),
    (d.basePoints ?? 0) + (d.maxAmount ?? 0) * per(d.maxHold ?? 0),
  ];
}

/** What the numbers mean in a session, so points can be tuned by feel. */
function PointsReadout({ kind, draft }: { kind: Kind; draft: Definition }) {
  if (kind === "enders") return null;
  const lines: string[] = [];
  if (kind === "methods" && !draft.timePointMultiplier && !draft.edgePointMultiplier) {
    lines.push("No points set yet: sessions estimate them from its intensity, torture and closeness.");
    lines.push("Set them here, or compare it in the Compare tab and apply your answers.");
  } else if (kind === "methods") {
    const minute = (draft.timePointMultiplier ?? 0) * 60;
    lines.push(`A minute on this grip earns about ${minute.toFixed(1)} points; an edge on it earns ${(draft.edgePointMultiplier ?? 0).toFixed(1)}.`);
    lines.push(`A typical session earns about ${PointsPerMinute} points a minute, so a minute here counts as ${(minute / PointsPerMinute).toFixed(1)} minutes of an average session.`);
  } else {
    const [low, high] = valueRange(draft);
    lines.push(`This deal is worth ${Math.round(low)} to ${Math.round(high)} session points, about ${(low / PointsPerMinute).toFixed(0)} to ${(high / PointsPerMinute).toFixed(0)} minutes of play.`);
    lines.push("She picks the amount so the price roughly matches what you asked for.");
  }
  return (
    <div className="rounded-xl border border-accent/30 bg-accent/10 px-4 py-3 text-sm text-foreground">
      {lines.map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  );
}

/**
 * A number the user types freely: shown rounded to four places, kept as text while typing (so
 * "0." survives), and passed on whenever it reads as a number.
 */
function NumberField({ label, value, onChange }: { label: string; value: number | undefined; onChange: (v: number) => void }) {
  const shown = (v: number | undefined) => (typeof v === "number" && Number.isFinite(v) ? String(Number(v.toFixed(4))) : "");
  const [text, setText] = useState(shown(value));
  useEffect(() => {
    if (Number(text) !== value) setText(shown(value));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value]);
  return (
    <input
      type="text"
      inputMode="decimal"
      aria-label={label}
      className={`${inputCls} tabular-nums`}
      value={text}
      onChange={(e) => {
        setText(e.target.value);
        const n = Number(e.target.value);
        if (e.target.value.trim() !== "" && Number.isFinite(n)) onChange(n);
      }}
    />
  );
}

/**
 * The tuning lab: her methods, punishment endings and deals, as shared defaults the user can
 * override for their own account, with what each is worth in session points.
 */
export function DefinitionSettings() {
  const [data, setData] = useState<Configuration | null>(null);
  const [kind, setKind] = useState<Kind>("methods");
  const [selected, setSelected] = useState(1);
  const [draft, setDraft] = useState<Definition | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [creating, setCreating] = useState(false);
  useEffect(() => {
    let active = true;
    request()
      .then((value) => {
        if (active) setData(value);
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, []);
  const view = data?.[kind]?.find((x) => x.value.id === selected);
  useEffect(() => {
    if (!creating) setDraft(view ? { ...view.value } : null);
  }, [view, kind, selected, creating]);
  const mutate = async (operation: () => Promise<any>, message: string, after?: (result: any) => void) => {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = await operation();
      setData(await request());
      after?.(result);
      setNotice(message);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  if (!data) return <p className="text-sm text-secondary">{error || "Loading definitions…"}</p>;
  const all = data[kind] ?? [];
  const list = all.filter((x) => !isRemoved(kind, x.value));
  const removed = all.filter((x) => isRemoved(kind, x.value));
  const noun = kind === "methods" ? "method" : kind === "enders" ? "punishment ending" : "deal";
  const setRemoved = (value: boolean) => {
    if (!draft) return;
    const operation =
      kind === "offers"
        ? () => request(`/offers/${draft.id}`, "PUT", { ...draft, enabled: !value })
        : () => request(`/${kind}/${draft.id}/${value ? "remove" : "restore"}`, "POST");
    void mutate(operation, value ? `Removed from your ${noun}s. She won't use it.` : "Back in your library.");
  };
  const pick = (id: number) => {
    setCreating(false);
    setSelected(id);
    setNotice("");
  };
  const listItem = (x: View) => (
    <button
      key={x.value.id}
      type="button"
      aria-current={!creating && x.value.id === selected}
      className={`block w-full rounded-lg px-3 py-2 text-left transition ${!creating && x.value.id === selected ? "bg-accent/15 ring-1 ring-accent/40" : "hover:bg-card-hover"}`}
      onClick={() => pick(x.value.id)}
    >
      <span className="flex items-center justify-between gap-2">
        <span className="truncate text-sm font-medium text-foreground">{x.value.name}</span>
        {x.isOverride && <span className="shrink-0 rounded-full bg-accent/20 px-2 py-0.5 text-[11px] font-semibold text-accent">personal</span>}
        {!x.isOverride && !x.sharedDefault && <span className="shrink-0 rounded-full bg-emerald-500/20 px-2 py-0.5 text-[11px] font-semibold text-emerald-300">yours</span>}
      </span>
      <span className="block truncate text-xs text-secondary">{summary(kind, x.value)}</span>
    </button>
  );
  const set = (key: string, value: any) => setDraft((d) => (d ? { ...d, [key]: value } : d));
  const groups: Record<string, string[][]> = { text: [], scale: [], choice: [], number: [], checkbox: [] };
  for (const field of fields[kind]) {
    const type = field[2];
    const group = type === "text" || type === "textarea" ? "text" : type === "scale" ? "scale" : type === "checkbox" ? "checkbox" : type === "number" ? "number" : "choice";
    groups[group].push(field);
  }
  const labelCls = "text-xs font-medium uppercase tracking-wide text-muted";

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex rounded-lg border border-border bg-surface p-0.5 text-sm" role="group" aria-label="Definition type">
          {kinds.map(([key, label]) => (
            <button
              key={key}
              type="button"
              aria-pressed={kind === key}
              className={`rounded-md px-3 py-1.5 ${kind === key ? "bg-accent text-white" : "text-secondary hover:text-foreground"}`}
              onClick={() => {
                setKind(key);
                setCreating(false);
                setSelected(data[key]?.[0]?.value.id || 1);
                setNotice("");
              }}
            >
              {label}
            </button>
          ))}
        </div>
        <p className="text-sm text-secondary">{kinds.find((k) => k[0] === kind)![2]}</p>
      </div>

      <div className="grid gap-4 lg:grid-cols-[18rem_minmax(0,1fr)]">
        <div className="space-y-2">
          <button
            type="button"
            className={`${secondaryBtn} w-full justify-center`}
            disabled={busy}
            onClick={() => {
              setCreating(true);
              setDraft({ ...blanks[kind] });
              setNotice("");
            }}
          >
            + New {noun}
          </button>
          <nav aria-label="Definitions" className="max-h-[70vh] space-y-1 overflow-y-auto rounded-2xl border border-border bg-card p-2">
            {list.length === 0 && <p className="px-3 py-6 text-sm text-secondary">Nothing here yet.</p>}
            {list.map(listItem)}
            {removed.length > 0 && (
              <>
                <div className="px-3 pb-1 pt-3 text-xs font-semibold uppercase tracking-wide text-muted">Removed</div>
                <div className="opacity-60">{removed.map(listItem)}</div>
              </>
            )}
          </nav>
        </div>

        {draft ? (
          <form
            className="space-y-5 rounded-2xl border border-border bg-card p-5"
            onSubmit={(e) => {
              e.preventDefault();
              if (creating)
                void mutate(() => request(`/${kind}`, "POST", draft), `Added to your ${noun}s.`, (made) => {
                  setCreating(false);
                  setSelected(made.id);
                });
              else void mutate(() => request(`/${kind}/${draft.id}`, "PUT", draft), view?.sharedDefault ? "Personal override saved." : "Saved.");
            }}
          >
            <fieldset disabled={busy} className="min-w-0 space-y-5 border-0 p-0">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <h2 className="text-lg font-semibold text-foreground">{draft.name || "Untitled"}</h2>
                <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${creating || view?.isOverride || !view?.sharedDefault ? "bg-accent/20 text-accent" : "bg-surface text-secondary"}`}>
                  {creating
                    ? "New, not saved yet"
                    : view && isRemoved(kind, view.value)
                      ? "Removed from your library"
                      : view?.isOverride
                        ? "Your personal override"
                        : view?.sharedDefault
                          ? "Using shared default"
                          : "Your own"}
                </span>
              </div>

              <div className="grid gap-4 md:grid-cols-2">
                {groups.text.map(([key, label, type]) => (
                  <label key={key} className={`space-y-1.5 ${type === "textarea" ? "md:col-span-2" : ""}`}>
                    <span className={labelCls}>{label}</span>
                    {type === "textarea" ? (
                      <textarea aria-label={label} rows={3} className={`${inputCls} resize-y`} value={draft[key] || ""} onChange={(e) => set(key, e.target.value)} />
                    ) : (
                      <input aria-label={label} className={inputCls} value={draft[key] ?? ""} onChange={(e) => set(key, e.target.value)} />
                    )}
                  </label>
                ))}
              </div>

              {groups.scale.length > 0 && (
                <Section title="How it feels">
                  <div className="grid gap-x-6 gap-y-3 md:grid-cols-2">
                    {groups.scale.map(([key, label]) => (
                      <div key={key} className="space-y-1">
                        <span className="text-sm text-foreground">{label}</span>
                        <Slider label={label} value={Number(draft[key] ?? 0)} min={0} max={10} onChange={(v) => set(key, v)} />
                      </div>
                    ))}
                  </div>
                </Section>
              )}

              {(groups.choice.length > 0 || groups.checkbox.length > 0) && (
                <Section title={kind === "offers" ? "The deal" : "Needs"}>
                  <div className="grid gap-3 md:grid-cols-2">
                    {groups.choice.map(([key, label, type]) => (
                      <label key={key} className="space-y-1.5">
                        <span className={labelCls}>{label}</span>
                        <select aria-label={label} className={`${selectCls} w-full`} value={draft[key]} onChange={(e) => set(key, e.target.value)}>
                          {choices[type].map((choice) => (
                            <option key={choice} value={choice}>
                              {choice}
                            </option>
                          ))}
                        </select>
                      </label>
                    ))}
                    {groups.checkbox.map(([key, label]) => (
                      <label key={key} className="flex items-center gap-3 rounded-xl border border-border bg-surface px-3 py-2.5">
                        <input
                          type="checkbox"
                          aria-label={label}
                          className="h-4 w-4 accent-[var(--color-accent)]"
                          checked={!!draft[key]}
                          onChange={(e) => set(key, e.target.checked)}
                        />
                        <span className="text-sm text-foreground">{label}</span>
                      </label>
                    ))}
                  </div>
                </Section>
              )}

              {groups.number.length > 0 && (
                <Section title="Points and amounts">
                  <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                    {groups.number.map(([key, label]) => (
                      <label key={key} className="space-y-1.5">
                        <span className={labelCls}>{label}</span>
                        <NumberField label={label} value={draft[key]} onChange={(v) => set(key, v)} />
                      </label>
                    ))}
                  </div>
                  <PointsReadout kind={kind} draft={draft} />
                </Section>
              )}

              <div className="flex flex-wrap gap-2 border-t border-border pt-4">
                <button type="submit" className={primaryBtn}>
                  {creating ? `Add to my ${noun}s` : view?.sharedDefault ? "Save personal override" : "Save"}
                </button>
                {creating ? (
                  <button type="button" className={secondaryBtn} onClick={() => setCreating(false)}>
                    Cancel
                  </button>
                ) : (
                  <>
                    {view?.sharedDefault && (
                      <button
                        type="button"
                        className={secondaryBtn}
                        disabled={!view?.isOverride}
                        onClick={() => void mutate(() => request(`/${kind}/${draft.id}/override`, "DELETE"), "Shared default restored.")}
                      >
                        Restore shared default
                      </button>
                    )}
                    {view && (
                      <button type="button" className={`${secondaryBtn} ml-auto`} onClick={() => setRemoved(!isRemoved(kind, view.value))}>
                        {isRemoved(kind, view.value) ? "Put back in my library" : `Remove this ${noun}`}
                      </button>
                    )}
                  </>
                )}
              </div>
            </fieldset>
          </form>
        ) : (
          <p className="rounded-2xl border border-dashed border-border px-4 py-6 text-sm text-secondary">Pick something on the left.</p>
        )}
      </div>

      <section className="flex flex-col gap-3 rounded-2xl border border-border bg-card p-5 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="text-xs font-semibold uppercase tracking-wide text-muted">Calibration</div>
          <p className="mt-1 text-sm text-secondary">
            {data.ratings.length} ratings and {data.comparisons.length} comparisons recorded. With 25 or more method comparisons,
            the optimizer sets each method's points from how they compared for you.
          </p>
        </div>
        <button
          type="button"
          className={`${secondaryBtn} shrink-0`}
          disabled={busy || data.comparisons.filter((x) => x.value.mode === "Stim").length < 25}
          onClick={() => void mutate(() => request("/optimize", "POST"), "Method multipliers optimized for your account.")}
        >
          Optimize my method multipliers
        </button>
      </section>
      <p className="text-xs text-muted">
        Shared defaults are available to every Cove account; saving creates your personal override. Changes apply from your next
        session.
      </p>
      {notice && <Notice tone="ok">{notice}</Notice>}
      {error && <Notice tone="error">{error}</Notice>}
    </div>
  );
}
