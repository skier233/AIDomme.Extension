import React, { useEffect, useState, useSyncExternalStore } from "react";
import { navigateToUrl } from "@cove/runtime/components";
import { guidedApi } from "./GuidedClient";
import { Row, SaveState, Section, Select, Slider, inputCls, secondaryBtn } from "./Ui";

type Persona = {
  harshness: number;
  humiliation: number;
  manipulation: number;
  talkativeness: number;
  variance: number;
  names: string[];
};
type Lengths = { length1: number; length5: number; length10: number };
type Endings = {
  denial: "Never" | "Rarely" | "Normal" | "Often" | "Mostly";
  ruins: "Never" | "PunishmentOnly" | "Sometimes" | "Often";
  orgasmEveryDays: number;
};
type Profile = {
  tolerance: "Briefly" | "AWhile" | "ALongTime";
  toleranceSeconds?: number | null;
  pace?: number | null;
  recovery?: number | null;
};
export type DommeSettings = { persona: Persona; endings: Endings; lengths: Lengths; profile: Profile };
type Status = "idle" | "saving" | "saved" | "error";

const defaultEndings: Endings = { denial: "Normal", ruins: "Sometimes", orgasmEveryDays: 0 };
const defaultLengths: Lengths = { length1: 10, length5: 35, length10: 90 };
const defaultProfile: Profile = { tolerance: "AWhile" };

/**
 * Her settings as one object shared by every panel that edits a part of it, so two panels never
 * save over each other. Changes apply at once and save shortly after the last one.
 */
class SettingsStore {
  private state: { settings: DommeSettings | null; status: Status; error: string } = {
    settings: null,
    status: "idle",
    error: "",
  };
  private listeners = new Set<() => void>();
  private loading: Promise<void> | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private revision = 0;

  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };
  get = () => this.state;
  private set(patch: Partial<SettingsStore["state"]>) {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  }

  load() {
    if (this.state.settings || this.loading) return;
    this.loading = guidedApi("/domme-settings")
      .then((value) => this.set({ settings: value.settings, error: "" }))
      .catch((e) => this.set({ error: (e as Error).message, status: "error" }))
      .finally(() => (this.loading = null));
  }

  update(change: (settings: DommeSettings) => DommeSettings, delay = 600) {
    if (!this.state.settings) return;
    this.set({ settings: change(this.state.settings), status: "saving", error: "" });
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.save(), delay);
  }

  private async save() {
    const revision = ++this.revision;
    try {
      const saved = await guidedApi("/domme-settings", this.state.settings, "PUT");
      if (revision === this.revision) this.set({ settings: saved, status: "saved" });
    } catch (e) {
      if (revision === this.revision) this.set({ status: "error", error: (e as Error).message });
    }
  }

  /** Tests start from nothing. */
  reset() {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.loading = null;
    this.revision++;
    this.state = { settings: null, status: "idle", error: "" };
  }
}

export const dommeSettings = new SettingsStore();

function useSettings() {
  const state = useSyncExternalStore(dommeSettings.subscribe, dommeSettings.get, dommeSettings.get);
  useEffect(() => dommeSettings.load(), []);
  return state;
}

function Loading({ error }: { error: string }) {
  return error ? (
    <p role="alert" className="text-sm text-red-400">
      {error}
    </p>
  ) : (
    <p className="text-sm text-secondary">Loading…</p>
  );
}

// ── Her persona ──

const dials: { key: "harshness" | "humiliation" | "manipulation" | "talkativeness"; label: string; hint: string }[] = [
  { key: "harshness", label: "Harshness", hint: "How harsh she sounds when her mood is neutral. Her mood moves it during a session." },
  { key: "humiliation", label: "Humiliation", hint: "How much she belittles you, and what she calls you when you leave names empty." },
  { key: "manipulation", label: "Mind games", hint: "Lying about the ending, false finishes, keeping you guessing." },
  { key: "talkativeness", label: "Talkativeness", hint: "How much she says besides her orders: teasing, remarks, stories." },
];

/** Who she is in every session: how she talks and plays, not how strict a session is. */
export function HerPersonaPanel() {
  const { settings, status, error } = useSettings();
  const [names, setNames] = React.useState<string | null>(null);
  if (!settings) return <Loading error={error} />;
  const persona = settings.persona;
  const setPersona = (patch: Partial<Persona>) => dommeSettings.update((s) => ({ ...s, persona: { ...s.persona, ...patch } }));
  const commitNames = () => {
    if (names === null) return;
    setPersona({ names: names.split(",").map((x) => x.trim()).filter(Boolean) });
    setNames(null);
  };
  return (
    <div className="aidomme-settings-panel space-y-3">
      <p className="text-sm text-secondary">
        Who she is in every session. How strict a session is comes from the session's own settings; these decide how she
        talks and plays with you. Each session she drifts a little around them.
      </p>
      {dials.map((dial) => (
        <Row key={dial.key} label={dial.label} hint={dial.hint}>
          <Slider label={dial.label} value={persona[dial.key]} min={0} max={10} onChange={(v) => setPersona({ [dial.key]: v })} />
        </Row>
      ))}
      <Row label="Session to session variety" hint="How far she strays from these settings from one session to the next.">
        <Slider
          label="Session to session variety"
          value={persona.variance}
          min={0}
          max={3}
          onChange={(v) => setPersona({ variance: v })}
          format={(v) => ["None", "A little", "Some", "A lot"][v] ?? String(v)}
        />
      </Row>
      <Row label="What she calls you" hint="Separate names with commas. Leave empty and she chooses, depending on humiliation.">
        <input
          aria-label="What she calls you"
          className={inputCls}
          placeholder="Her choice"
          value={names ?? persona.names.join(", ")}
          onChange={(e) => setNames(e.target.value)}
          onBlur={commitNames}
          onKeyDown={(e) => e.key === "Enter" && commitNames()}
        />
      </Row>
      <SaveState status={status} error={error} />
    </div>
  );
}

// ── How sessions end ──

const denialChoices = [
  ["Never", "Never, unless I promised"],
  ["Rarely", "Rarely"],
  ["Normal", "Sometimes"],
  ["Often", "Often"],
  ["Mostly", "Mostly: orgasms are a treat"],
] as const;
const ruinChoices = [
  ["Never", "Never"],
  ["PunishmentOnly", "Only as a price or punishment"],
  ["Sometimes", "Sometimes, her choice"],
  ["Often", "Often"],
] as const;

/** His general preferences for endings; each session's Denial setting and limits apply on top. */
export function EndingsPanel() {
  const { settings, status, error } = useSettings();
  if (!settings) return <Loading error={error} />;
  const endings = settings.endings ?? defaultEndings;
  const setEndings = (patch: Partial<Endings>) =>
    dommeSettings.update((s) => ({ ...s, endings: { ...(s.endings ?? defaultEndings), ...patch } }));
  return (
    <div className="aidomme-settings-panel space-y-3">
      <p className="text-sm text-secondary">
        Your general preferences. Each session's Denial setting still moves her within them, and what you do still changes her
        mind. Promises and denial you agree to always come first.
      </p>
      <Row label="Denial at the end of a session" hint="Never means she lets you cum at the end unless you promised otherwise.">
        <Select label="Denial at the end of a session" value={endings.denial} options={denialChoices} onChange={(v) => setEndings({ denial: v })} />
      </Row>
      <Row label="Ruined orgasms" hint="A price or punishment covers deals you accept, what you owe, and punishment sessions.">
        <Select label="Ruined orgasms" value={endings.ruins} options={ruinChoices} onChange={(v) => setEndings({ ruins: v })} />
      </Row>
      <Row label="Days between orgasms" hint="Well before this she mostly says no; after it she comes round.">
        <Slider
          label="Days between orgasms"
          value={endings.orgasmEveryDays}
          min={0}
          max={30}
          onChange={(v) => setEndings({ orgasmEveryDays: v })}
          format={(v) => (v === 0 ? "No preference" : v === 1 ? "1 day" : `${v} days`)}
        />
      </Row>
      <SaveState status={status} error={error} />
    </div>
  );
}

// ── Session length ──

const lengthDials: { key: keyof Lengths; label: string; min: number; max: number; step: number }[] = [
  { key: "length1", label: "Length 1", min: 3, max: 60, step: 1 },
  { key: "length5", label: "Length 5", min: 10, max: 180, step: 5 },
  { key: "length10", label: "Length 10", min: 20, max: 360, step: 5 },
];

/** What the Length setting means to him, in minutes of play. */
export function LengthsPanel() {
  const { settings, status, error } = useSettings();
  if (!settings) return <Loading error={error} />;
  const lengths = settings.lengths ?? defaultLengths;
  return (
    <div className="aidomme-settings-panel space-y-3">
      <p className="text-sm text-secondary">
        Roughly how many minutes of play each Length means to you; the lengths between follow. She follows your pace as the
        session goes, so a session lands near these minutes; one full of edges still ends a little sooner than one full of slow
        teasing, and a deal you accept to cum early ends it when you have paid.
      </p>
      {lengthDials.map((dial) => (
        <Row key={dial.key} label={dial.label}>
          <Slider
            label={`${dial.label} in minutes`}
            value={lengths[dial.key]}
            min={dial.min}
            max={dial.max}
            step={dial.step}
            onChange={(v) => dommeSettings.update((s) => ({ ...s, lengths: { ...(s.lengths ?? defaultLengths), [dial.key]: v } }))}
            format={(v) => `${v} min`}
          />
        </Row>
      ))}
      <SaveState status={status} error={error} />
    </div>
  );
}

// ── Your body ──

const toleranceChoices = [
  ["Briefly", "Briefly: a few seconds at the edge and I'm in trouble"],
  ["AWhile", "A while: I can sit at the edge for a bit"],
  ["ALongTime", "A long time: I can stay there"],
] as const;

/** The one thing about his body she asks: how long he can stay at the edge. */
export function BodyPanel() {
  const { settings, status, error } = useSettings();
  if (!settings) return <Loading error={error} />;
  const profile = settings.profile ?? defaultProfile;
  return (
    <div className="aidomme-settings-panel space-y-3">
      <p className="text-sm text-secondary">
        She tracks how close you are, how much you need it and how much control you have left, and steers the session by them.
        How fast you get to the edge and how fast you come down she learns from your edges within a few of them. How long you can
        stay at the edge she cannot see until it is too late, so tell her. She sizes holds and decides when to ease off from this,
        and adjusts it slowly from what you take.
      </p>
      <Row label="I can stay at the edge" hint="Used for holds and for how hard she pushes near the edge. More intense methods count for more.">
        <Select
          label="How long you can stay at the edge"
          value={profile.tolerance}
          options={toleranceChoices}
          onChange={(v) => dommeSettings.update((s) => ({ ...s, profile: { ...(s.profile ?? defaultProfile), tolerance: v, toleranceSeconds: null } }))}
        />
      </Row>
      <p className="text-xs text-secondary">
        The exact numbers she is working with, where each came from, and a way to set or reset them are in the tuning lab under
        Your body.
      </p>
      <SaveState status={status} error={error} />
    </div>
  );
}

type ProfileValue = { value: number; source: string; default: number };
type ProfileView = {
  settings: Profile;
  pace: ProfileValue;
  recovery: ProfileValue;
  tolerance: ProfileValue;
  levels: { level: string; seconds: number }[];
  hasLearned: boolean;
};

const profileRows: { key: "pace" | "recovery" | "tolerance"; field: "pace" | "recovery" | "toleranceSeconds"; label: string; hint: string; min: number; max: number; step: number; unit: string }[] = [
  {
    key: "pace", field: "pace", label: "Pace", unit: "×", min: 0.3, max: 3, step: 0.05,
    hint: "A factor on how long every method takes you to edge. 1 is typical; lower means you get there faster.",
  },
  {
    key: "recovery", field: "recovery", label: "Recovery", unit: "s", min: 5, max: 180, step: 1,
    hint: "Seconds of hands off for your closeness to halve. She sizes the gaps between edges from this.",
  },
  {
    key: "tolerance", field: "toleranceSeconds", label: "Tolerance", unit: "s", min: 5, max: 300, step: 1,
    hint: "Seconds you can stay at the edge on a method of middling intensity before you are likely to go over.",
  },
];

/** The tuning lab's view of his body as she has it: each value, where it came from, and a way to set or reset it. */
export function ProfileLab() {
  const [view, setView] = useState<ProfileView | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const run = async (work: () => Promise<ProfileView>) => {
    setBusy(true);
    setError("");
    try {
      setView(await work());
      // The settings panels hold their own copy; they load again when next opened.
      dommeSettings.reset();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  useEffect(() => {
    void run(() => guidedApi("/profile"));
  }, []);
  if (!view) return <Loading error={error} />;
  const save = (patch: Partial<Profile>) => run(() => guidedApi("/profile", { ...view.settings, ...patch }, "PUT"));
  return (
    <div className="space-y-4">
      <p className="text-sm text-secondary">
        What she believes about your body. She estimates how close you are from these and corrects herself every time you report
        an edge. Pace and recovery she learns on her own; leave them alone unless they are plainly wrong. A value you set here
        stays fixed until you clear it.
      </p>
      <div className="overflow-hidden rounded-xl border border-border">
        {profileRows.map((row) => {
          const current = view[row.key];
          const fixed = view.settings[row.field];
          return (
            <div key={row.key} className="grid items-center gap-3 border-t border-border bg-card px-4 py-3 first:border-t-0 sm:grid-cols-[minmax(0,1fr)_10rem_auto]">
              <div className="min-w-0">
                <div className="text-sm font-medium text-foreground">
                  {row.label}: {current.value}
                  {row.unit === "s" ? " seconds" : ""}
                </div>
                <div className="text-xs text-secondary">
                  {row.hint} <span className="text-muted">Source: {current.source}.</span>
                </div>
              </div>
              <input
                key={`${row.key}-${fixed ?? "auto"}`}
                type="number"
                aria-label={`Set ${row.label.toLowerCase()}`}
                className={inputCls}
                min={row.min}
                max={row.max}
                step={row.step}
                defaultValue={fixed ?? ""}
                placeholder={fixed == null ? "automatic" : undefined}
                disabled={busy}
                onBlur={(e) => {
                  const value = e.target.value.trim() === "" ? null : Number(e.target.value);
                  if (value !== (fixed ?? null)) void save({ [row.field]: value });
                }}
              />
              <button type="button" className={secondaryBtn} disabled={busy || fixed == null} onClick={() => void save({ [row.field]: null })}>
                Clear
              </button>
            </div>
          );
        })}
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          className={secondaryBtn}
          disabled={busy || !view.hasLearned}
          onClick={() => void run(() => guidedApi("/profile/learned", undefined, "DELETE"))}
        >
          Forget what she has learned
        </button>
        <span className="text-xs text-secondary">
          {view.hasLearned ? "She starts again from the defaults and your tolerance setting." : "Nothing learned yet: these are the defaults and your setting."}
        </span>
      </div>
      {error && (
        <p role="alert" className="text-sm text-red-400">
          {error}
        </p>
      )}
    </div>
  );
}

/** The way into the tuning lab, from Cove's settings. */
export function TuningLabLinkPanel() {
  return (
    <div className="aidomme-settings-panel flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <p className="text-sm text-secondary">
        Her methods, punishment endings and deals: what they ask of you, how intense they are, and how many points each is worth.
        Adjust them, compare them, and calibrate the points to how they feel for you.
      </p>
      <button type="button" className={`${secondaryBtn} shrink-0`} onClick={() => navigateToUrl("/aidomme-tuning")}>
        Open the tuning lab
      </button>
    </div>
  );
}

// ── Debug ──

const pct = (value: number) => `${Math.round((value ?? 0) * 100)}%`;

/** Debug sessions only: his state as she has it, her aims and segments with her reasons, and the ending she leans toward. */
export function PlanDebug({ plan }: { plan: any }) {
  if (!plan) return null;
  const persona = plan.persona || {};
  const statusCls: Record<string, string> = {
    done: "text-muted line-through decoration-muted/40",
    now: "font-semibold text-accent",
    planned: "text-foreground",
  };
  return (
    <details className="space-y-3">
      <summary className="cursor-pointer text-sm font-medium text-foreground">Her plan (debug)</summary>
      <div className="mt-3 grid gap-2 text-sm text-secondary sm:grid-cols-2">
        <p>
          Mood <b className="text-foreground">{plan.mood}</b> ({plan.moodCause}), annoyance {plan.annoyance}. Points {plan.points} of{" "}
          {plan.budget}.
        </p>
        <p>
          Tonight: harshness {persona.harshness}, humiliation {persona.humiliation}, mind games {persona.manipulation},
          talkativeness {persona.talkativeness}.
        </p>
        <p className="sm:col-span-2">
          Ending she leans toward: <b className="text-foreground">{plan.ending}</b> ({plan.endingReason}).{" "}
          {plan.presentation === "Honest"
            ? "Her hints will be honest."
            : plan.presentation === "Lie"
              ? "Her hints may be lies."
              : "She will keep it to herself."}
        </p>
      </div>
      {plan.body && (
        <p className="mt-2 text-sm text-secondary">
          His state as she has it: closeness <b className="text-foreground">{pct(plan.body.closeness)}</b>, need{" "}
          <b className="text-foreground">{pct(plan.body.need)}</b>, control <b className="text-foreground">{pct(plan.body.control)}</b>,
          overwhelm <b className="text-foreground">{pct(plan.body.overwhelm)}</b>. Profile: pace {plan.body.pace} ({plan.body.paceSource}),
          recovery {plan.body.recovery}s ({plan.body.recoverySource}), tolerance {plan.body.tolerance}s ({plan.body.toleranceSource}).
        </p>
      )}
      <ol className="mt-2 space-y-2 text-sm">
        {(plan.aims || []).map((aim: any, i: number) => (
          <li key={i} className={statusCls[aim.status] ?? ""}>
            <b>{aim.name}</b>
            {aim.written && <em> as {aim.written}</em>} {Math.round(aim.share * 100)}%{aim.origin !== "plan" && <em> ({aim.origin})</em>}
            {aim.status === "now" && <strong> now</strong>}
            <div className="text-xs text-secondary no-underline">
              She wants: {aim.goal}. Why: {aim.why}.{aim.ended && ` Ended: ${aim.ended}.`}
            </div>
            <ul className="ml-4 mt-1 space-y-1">
              {(plan.segments || [])
                .filter((segment: any) => segment.aim === i)
                .map((segment: any, k: number) => (
                  <li key={k} className={statusCls[segment.status] ?? ""}>
                    <b>{segment.name}</b> ({segment.purpose}, {String(segment.role).toLowerCase()}, {segment.written ? "written" : "improvised"})
                    {segment.steps > 0 && `, ${segment.steps} step${segment.steps === 1 ? "" : "s"}`}
                    {segment.origin !== "plan" && <em> ({segment.origin})</em>}
                    {segment.status === "now" && <strong> now</strong>}
                    <div className="text-xs text-secondary">
                      {segment.parameters}. Why: {segment.reason}.{segment.ended && ` Ended: ${segment.ended}.`}
                    </div>
                  </li>
                ))}
            </ul>
          </li>
        ))}
      </ol>
      {plan.revisions?.length > 0 && (
        <Section title="How the plan changed">
          <ul className="space-y-1 text-xs text-secondary">
            {plan.revisions.map((revision: string, i: number) => (
              <li key={i}>{revision}</li>
            ))}
          </ul>
        </Section>
      )}
    </details>
  );
}
