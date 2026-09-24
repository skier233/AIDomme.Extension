import React from "react";
import { guidedApi } from "./GuidedClient";

const { useEffect, useState } = React;

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
type Settings = { persona: Persona; endings: Endings; lengths: Lengths; segments: Record<string, Preference> };

const denialChoices: [Endings["denial"], string][] = [
  ["Never", "Never: let me cum at the end unless I promised otherwise"],
  ["Rarely", "Rarely"],
  ["Normal", "Sometimes"],
  ["Often", "Often"],
  ["Mostly", "Mostly: orgasms are a rare treat"],
];
const ruinChoices: [Endings["ruins"], string][] = [
  ["Never", "Never"],
  ["PunishmentOnly", "Only as a price or a punishment"],
  ["Sometimes", "Sometimes, when she feels like it"],
  ["Often", "Often"],
];

const lengthDials: { key: keyof Lengths; label: string; min: number; max: number }[] = [
  { key: "length1", label: "Length 1", min: 3, max: 60 },
  { key: "length5", label: "Length 5", min: 10, max: 180 },
  { key: "length10", label: "Length 10", min: 20, max: 360 },
];
type SegmentInfo = { id: string; name: string; goal: string; tags: string[] };

const dials: { key: keyof Persona; label: string; hint: string }[] = [
  {
    key: "harshness",
    label: "Harshness",
    hint: "How harsh she sounds when her mood is neutral. Her mood moves it during a session.",
  },
  {
    key: "humiliation",
    label: "Humiliation",
    hint: "How much she belittles you, and what she calls you when you leave names empty.",
  },
  {
    key: "manipulation",
    label: "Mind games",
    hint: "Lying about the ending, false finishes, keeping you guessing.",
  },
  {
    key: "talkativeness",
    label: "Talkativeness",
    hint: "How much she says besides her orders: teasing, remarks, stories.",
  },
];

const frequencies: Frequency[] = ["Never", "Rarely", "Normally", "Often"];
const lengths: Length[] = ["Shorter", "Normal", "Longer"];

/**
 * Her persona and how she plans: set once, used by every session. Each session she drifts a
 * little around these settings, and remembers how the last one went.
 */
export function HerSettings() {
  const [settings, setSettings] = useState<Settings | null>(null);
  const [segments, setSegments] = useState<SegmentInfo[]>([]);
  const [names, setNames] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  useEffect(() => {
    let active = true;
    guidedApi("/domme-settings")
      .then((value) => {
        if (!active) return;
        setSettings(value.settings);
        setSegments(value.segments || []);
        setNames((value.settings.persona.names || []).join(", "));
      })
      .catch((e) => {
        if (active) setError((e as Error).message);
      });
    return () => {
      active = false;
    };
  }, []);
  if (!settings) return <p>{error || "Loading her settings…"}</p>;
  const persona = settings.persona;
  const minutes = settings.lengths || { length1: 10, length5: 35, length10: 90 };
  const endings: Endings = settings.endings || { denial: "Normal", ruins: "Sometimes", orgasmEveryDays: 0 };
  const setEndings = (patch: Partial<Endings>) => {
    setSettings({ ...settings, endings: { ...endings, ...patch } });
    setNotice("");
  };
  const setLength = (key: keyof Lengths, value: number) => {
    setSettings({ ...settings, lengths: { ...minutes, [key]: value } });
    setNotice("");
  };
  const setPersona = (patch: Partial<Persona>) => {
    setSettings({ ...settings, persona: { ...persona, ...patch } });
    setNotice("");
  };
  const preference = (id: string): Preference =>
    settings.segments[id] || { frequency: "Normally", length: "Normal" };
  const setPreference = (id: string, patch: Partial<Preference>) => {
    setSettings({
      ...settings,
      segments: { ...settings.segments, [id]: { ...preference(id), ...patch } },
    });
    setNotice("");
  };
  const save = async () => {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const body: Settings = {
        ...settings,
        persona: {
          ...persona,
          names: names
            .split(",")
            .map((x) => x.trim())
            .filter(Boolean),
        },
      };
      const saved = await guidedApi("/domme-settings", body, "PUT");
      setSettings(saved);
      setNames((saved.persona.names || []).join(", "));
      setNotice("Saved. New sessions use these settings.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="aidomme-card aidomme-her-settings">
      <h2>Her persona</h2>
      <p className="aidomme-muted">
        Who she is, in every session. How strict a session is comes from the
        session settings; these decide how she talks and plays with you.
      </p>
      <fieldset disabled={busy}>
        {dials.map((dial) => (
          <label key={dial.key} className="aidomme-dial">
            <span>
              {dial.label} <output>{persona[dial.key] as number}</output>
            </span>
            <input
              type="range"
              min={0}
              max={10}
              value={persona[dial.key] as number}
              aria-label={dial.label}
              onChange={(e) =>
                setPersona({ [dial.key]: Number(e.target.value) } as Partial<Persona>)
              }
            />
            <small className="aidomme-muted">{dial.hint}</small>
          </label>
        ))}
        <label className="aidomme-dial">
          <span>
            Session to session variety <output>{persona.variance}</output>
          </span>
          <input
            type="range"
            min={0}
            max={3}
            value={persona.variance}
            aria-label="Session to session variety"
            onChange={(e) => setPersona({ variance: Number(e.target.value) })}
          />
          <small className="aidomme-muted">
            How far she strays from these settings from one session to the next.
          </small>
        </label>
        <label>
          What she calls you
          <input
            value={names}
            placeholder="Her choice"
            aria-label="What she calls you"
            onChange={(e) => {
              setNames(e.target.value);
              setNotice("");
            }}
          />
          <small className="aidomme-muted">Separate names with commas.</small>
        </label>
      </fieldset>

      <h2>How your sessions end</h2>
      <p className="aidomme-muted">
        Your general preferences. Each session's Denial setting still moves her
        within them, and what you do still changes her mind. Promises and
        denial you agree to always come first.
      </p>
      <fieldset disabled={busy}>
        <label>
          Denial at the end of a session
          <select
            aria-label="Denial at the end of a session"
            value={endings.denial}
            onChange={(e) => setEndings({ denial: e.target.value as Endings["denial"] })}
          >
            {denialChoices.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label>
          Ruined orgasms
          <select
            aria-label="Ruined orgasms"
            value={endings.ruins}
            onChange={(e) => setEndings({ ruins: e.target.value as Endings["ruins"] })}
          >
            {ruinChoices.map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </select>
        </label>
        <label className="aidomme-dial">
          <span>
            Days between orgasms{" "}
            <output>{endings.orgasmEveryDays > 0 ? `about ${endings.orgasmEveryDays}` : "no preference"}</output>
          </span>
          <input
            type="range"
            min={0}
            max={30}
            value={endings.orgasmEveryDays}
            aria-label="Days between orgasms"
            onChange={(e) => setEndings({ orgasmEveryDays: Number(e.target.value) })}
          />
          <small className="aidomme-muted">
            Well before this she mostly says no; after it she comes round.
          </small>
        </label>
      </fieldset>

      <h2>Session length</h2>
      <p className="aidomme-muted">
        Roughly how many minutes of play each Length means to you; the lengths
        between follow. She still runs on what you do, not the clock: a session
        full of edges ends sooner than one full of slow teasing, and she learns
        your pace from the sessions you finish.
      </p>
      <fieldset disabled={busy}>
        {lengthDials.map((dial) => (
          <label key={dial.key} className="aidomme-dial">
            <span>
              {dial.label} <output>{minutes[dial.key]} min</output>
            </span>
            <input
              type="range"
              min={dial.min}
              max={dial.max}
              step={dial.max > 60 ? 5 : 1}
              value={minutes[dial.key]}
              aria-label={`${dial.label} in minutes`}
              onChange={(e) => setLength(dial.key, Number(e.target.value))}
            />
          </label>
        ))}
      </fieldset>

      <h2>What she plans</h2>
      <p className="aidomme-muted">
        A session is a series of segments she chooses as she goes. Tell her
        which ones you want more or less of, and how long they should run.
      </p>
      <fieldset disabled={busy}>
        <table className="aidomme-segments">
          <thead>
            <tr>
              <th>Segment</th>
              <th>How often</th>
              <th>Length</th>
            </tr>
          </thead>
          <tbody>
            {segments.map((segment) => (
              <tr key={segment.id}>
                <td>
                  <strong>{segment.name}</strong>
                  <br />
                  <small className="aidomme-muted">{segment.goal}</small>
                </td>
                <td>
                  <select
                    aria-label={`${segment.name}: how often`}
                    value={preference(segment.id).frequency}
                    onChange={(e) =>
                      setPreference(segment.id, {
                        frequency: e.target.value as Frequency,
                      })
                    }
                  >
                    {frequencies.map((x) => (
                      <option key={x}>{x}</option>
                    ))}
                  </select>
                </td>
                <td>
                  <select
                    aria-label={`${segment.name}: length`}
                    value={preference(segment.id).length}
                    disabled={preference(segment.id).frequency === "Never"}
                    onChange={(e) =>
                      setPreference(segment.id, { length: e.target.value as Length })
                    }
                  >
                    {lengths.map((x) => (
                      <option key={x}>{x}</option>
                    ))}
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </fieldset>
      <div className="aidomme-actions">
        <button disabled={busy} onClick={() => void save()}>
          Save her settings
        </button>
      </div>
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
    </div>
  );
}

/** Debug sessions only: her plan, what she leans toward for the ending, and why. */
export function PlanDebug({ plan }: { plan: any }) {
  if (!plan) return null;
  const persona = plan.persona || {};
  return (
    <details className="aidomme-plan-debug">
      <summary>Her plan (debug)</summary>
      <p>
        Mood <b>{plan.mood}</b> ({plan.moodCause}), annoyance {plan.annoyance}.
        Points {plan.points} of {plan.budget}.
      </p>
      <p>
        Tonight: harshness {persona.harshness}, humiliation {persona.humiliation},
        mind games {persona.manipulation}, talkativeness {persona.talkativeness}.
      </p>
      <p>
        Ending she leans toward: <b>{plan.ending}</b> ({plan.endingReason}).{" "}
        {plan.presentation === "Honest"
          ? "Her hints will be honest."
          : plan.presentation === "Lie"
            ? "Her hints may be lies."
            : "She will keep it to herself."}
      </p>
      <ol className="aidomme-plan-segments">
        {(plan.segments || []).map((segment: any, i: number) => (
          <li key={i} className={`is-${segment.status}`}>
            <b>{segment.name}</b> {Math.round(segment.share * 100)}%
            {segment.steps > 0 && `, ${segment.steps} step${segment.steps === 1 ? "" : "s"}`}
            {segment.origin !== "plan" && <em> ({segment.origin})</em>}
            {segment.status === "now" && <strong> now</strong>}
          </li>
        ))}
      </ol>
      {plan.revisions?.length > 0 && (
        <>
          <p>How the plan changed:</p>
          <ul>
            {plan.revisions.map((revision: string, i: number) => (
              <li key={i}>{revision}</li>
            ))}
          </ul>
        </>
      )}
    </details>
  );
}
