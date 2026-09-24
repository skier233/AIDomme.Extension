import React, { useEffect, useSyncExternalStore } from "react";
import { navigateToUrl } from "@cove/runtime/components";
import { guidedApi } from "./GuidedClient";
import { Row, SaveState, Section, Select, Slider, inputCls, secondaryBtn } from "./Ui";

type Frequency = "Never" | "Rarely" | "Normally" | "Often";
type Length = "Shorter" | "Normal" | "Longer";
type Preference = { frequency: Frequency; length: Length };
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
export type DommeSettings = { persona: Persona; endings: Endings; lengths: Lengths; segments: Record<string, Preference> };
type SegmentInfo = { id: string; name: string; goal: string; tags: string[] };
type Status = "idle" | "saving" | "saved" | "error";

const defaultEndings: Endings = { denial: "Normal", ruins: "Sometimes", orgasmEveryDays: 0 };
const defaultLengths: Lengths = { length1: 10, length5: 35, length10: 90 };

/**
 * Her settings as one object shared by every panel that edits a part of it, so two panels never
 * save over each other. Changes apply at once and save shortly after the last one.
 */
class SettingsStore {
  private state: { settings: DommeSettings | null; segments: SegmentInfo[]; status: Status; error: string } = {
    settings: null,
    segments: [],
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
      .then((value) => this.set({ settings: value.settings, segments: value.segments || [], error: "" }))
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
    this.state = { settings: null, segments: [], status: "idle", error: "" };
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
        Roughly how many minutes of play each Length means to you; the lengths between follow. She still runs on what you do,
        not the clock: a session full of edges ends sooner than one full of slow teasing. She learns your pace from the sessions
        you finish.
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

// ── What she plans ──

const frequencies = [
  ["Never", "Never"],
  ["Rarely", "Rarely"],
  ["Normally", "Normally"],
  ["Often", "Often"],
] as const;
const lengthChoices = [
  ["Shorter", "Shorter"],
  ["Normal", "Normal"],
  ["Longer", "Longer"],
] as const;

/** How often she uses each kind of segment, and how long it runs. */
export function SegmentsPanel() {
  const { settings, segments, status, error } = useSettings();
  if (!settings) return <Loading error={error} />;
  const preference = (id: string): Preference => settings.segments[id] || { frequency: "Normally", length: "Normal" };
  const setPreference = (id: string, patch: Partial<Preference>) =>
    dommeSettings.update((s) => ({
      ...s,
      segments: { ...s.segments, [id]: { ...(s.segments[id] || { frequency: "Normally", length: "Normal" }), ...patch } },
    }));
  return (
    <div className="aidomme-settings-panel space-y-3">
      <p className="text-sm text-secondary">
        A session is a series of segments she chooses as she goes. Tell her which ones you want more or less of, and how long
        they should run.
      </p>
      <div className="overflow-hidden rounded-xl border border-border">
        <div className="hidden grid-cols-[minmax(0,1fr)_9rem_9rem] gap-3 bg-card/60 px-4 py-2 text-xs font-medium uppercase tracking-wide text-muted sm:grid">
          <span>Segment</span>
          <span>How often</span>
          <span>Length</span>
        </div>
        {segments.map((segment) => {
          const p = preference(segment.id);
          return (
            <div
              key={segment.id}
              className="grid grid-cols-2 items-center gap-3 border-t border-border bg-card px-4 py-3 first:border-t-0 sm:grid-cols-[minmax(0,1fr)_9rem_9rem]"
            >
              <div className="col-span-2 min-w-0 sm:col-span-1">
                <div className="text-sm font-medium text-foreground">{segment.name}</div>
                <div className="text-xs text-secondary">{segment.goal}</div>
              </div>
              <Select
                label={`${segment.name}: how often`}
                value={p.frequency}
                options={frequencies}
                onChange={(v) => setPreference(segment.id, { frequency: v })}
              />
              <Select
                label={`${segment.name}: length`}
                value={p.length}
                options={lengthChoices}
                disabled={p.frequency === "Never"}
                onChange={(v) => setPreference(segment.id, { length: v })}
              />
            </div>
          );
        })}
      </div>
      <SaveState status={status} error={error} />
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
      <button type="button" className={`${secondaryBtn} shrink-0`} onClick={() => navigateToUrl("/aidome-tuning")}>
        Open the tuning lab
      </button>
    </div>
  );
}

// ── Debug ──

/** Debug sessions only: her plan, what she leans toward for the ending, and why. */
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
      <ol className="mt-2 space-y-1 text-sm">
        {(plan.segments || []).map((segment: any, i: number) => (
          <li key={i} className={statusCls[segment.status] ?? ""}>
            <b>{segment.name}</b> {Math.round(segment.share * 100)}%
            {segment.steps > 0 && `, ${segment.steps} step${segment.steps === 1 ? "" : "s"}`}
            {segment.origin !== "plan" && <em> ({segment.origin})</em>}
            {segment.status === "now" && <strong> now</strong>}
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
