import React from "react";
import {
  Field,
  TextInput,
  NumberInput,
  SaveButton,
  VideoPlayer,
  navigateToUrl,
} from "@cove/runtime/components";
import { extensionFetch } from "@cove/runtime/api";
export { TrackerCompanion } from "./Tracker";
import { TrackerCompanion, openTracker } from "./Tracker";
import { DefinitionSettings } from "./Configuration";
import { guidedApi, guidedClient, useGuidedSession } from "./GuidedClient";
import { FollowUps, LedgerPanel, Obligations, OfferCard, PromiseForm } from "./Ledger";
import {
  ChatLog,
  GameControls,
  GuidedMenu,
  PendingRequest,
  SessionActions,
  SessionStatus,
} from "./SessionControls";

const { useEffect, useMemo, useState } = React;
const API_BASE = "/api/ext/aidome";

type Authority = "Domme" | "User";
type Contract = {
  version: number;
  mode: string;
  content: Authority;
  methods: Authority;
  pacing: Authority;
  edges: Authority;
  games: boolean;
  preferences: { intensity: number; length: number; denial: number; mean: number };
  limits: {
    toys: string[];
    anal: boolean;
    bondage: boolean;
    cei: boolean;
    selfOral: boolean;
    allowedMethodIds: number[];
  };
  punishment: boolean;
  debug: boolean;
  gamesOnly: boolean;
  canChangeDuringSession: boolean;
};

const TOYS: [string, string][] = [
  ["HeadVibrator", "Vibrator"],
  ["Fleshlight", "Fleshlight"],
  ["Autoblow", "Autoblow"],
  ["ProstateMassager", "Prostate massager"],
];
const PREFERENCES: [keyof Contract["preferences"], string, string][] = [
  ["intensity", "Intensity", "How hard she pushes."],
  ["length", "Length", "How long before she decides your ending."],
  ["denial", "Denial", "How likely she is to say no."],
  ["mean", "Meanness", "How cruel she allows herself to be."],
];

// ═══════════════════════════ COMMAND CENTER PAGE ═══════════════════════════
export function CommandCenterPage() {
  const state = useGuidedSession();
  const session = state.snapshot?.sessionActive
    ? state.snapshot.session
    : null;
  return (
    <div className="aidomme-shell">
      <div className="aidomme-page-header">
        <h1>AI Domme</h1>
        <span className="aidomme-muted">
          {session
            ? `${modeName(state.setup, session.mode)}${session.isPaused ? " · paused" : ""}`
            : "Choose how you want to play"}
        </span>
        <button type="button" onClick={openTracker}>
          Open companion
        </button>
      </div>
      {state.error && <div className="aidomme-error">{state.error}</div>}
      {session ? <SessionScreen session={session} /> : <SetupScreen />}
    </div>
  );
}

function modeName(setup: any, mode: string) {
  return (
    setup?.modes?.find((m: any) => m.mode === mode)?.name ??
    (mode === "Custom" ? "Custom session" : mode)
  );
}

// ─────────────────────────── Setup ───────────────────────────
function SetupScreen() {
  const state = useGuidedSession();
  const setup = state.setup;
  const [mode, setMode] = useState("FullGuided");
  const [contract, setContract] = useState<Contract | null>(null);
  const [methods, setMethods] = useState<any[]>([]);
  useEffect(() => {
    if (!contract && setup?.modes?.length)
      setContract(fresh(setup.modes[0].contract));
  }, [setup, contract]);
  useEffect(() => {
    let alive = true;
    guidedApi("/configuration")
      .then((data) => {
        if (alive) setMethods(data.methods.map((item: any) => item.value));
      })
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);
  if (!setup || !contract) return <p className="aidomme-muted">Loading…</p>;
  const questions = state.snapshot?.followUps?.length > 0;

  const patch = (data: Partial<Contract>) =>
    setContract({ ...contract, ...data });
  const choose = (preset: any) => {
    setMode(preset.mode);
    // Keep what the user already set; take the authorities from the preset.
    setContract({
      ...contract,
      mode: preset.mode,
      content: preset.contract.content,
      methods: preset.contract.methods,
      pacing: preset.contract.pacing,
      edges: preset.contract.edges,
      games: preset.contract.games,
    });
  };
  const defaults = setup.defaults;
  const start = async () => {
    const response = await guidedClient.mutate("/session/start", {
      contract,
    });
    if (response?.sessionActive) openTracker();
  };

  return (
    <>
      {questions && (
        <section className="aidomme-card">
          <h2>Before anything else</h2>
          <FollowUps />
        </section>
      )}
      <section className="aidomme-card">
        <h2>How do you want to play?</h2>
        <div className="aidomme-modes" role="group" aria-label="Session mode">
          {setup.modes.map((preset: any) => (
            <button
              key={preset.mode}
              type="button"
              className="aidomme-mode"
              aria-pressed={mode === preset.mode}
              onClick={() => choose(preset)}
            >
              <strong>{preset.name}</strong>
              <span>{preset.description}</span>
            </button>
          ))}
        </div>
      </section>

      <section className="aidomme-card">
        <h2>Tonight</h2>
        <div className="aidomme-fields">
          {PREFERENCES.map(([key, label, hint]) => {
            const value = contract.preferences[key];
            return (
              <label className="aidomme-slider" key={key} title={hint}>
                <span>{label}</span>
                <output>
                  {value < 1 ? `Her choice (${defaults[key]})` : value}
                </output>
                <input
                  type="range"
                  min={0}
                  max={10}
                  value={value < 1 ? 0 : value}
                  aria-label={label}
                  onChange={(e) => {
                    const v = Number(e.target.value);
                    patch({
                      preferences: {
                        ...contract.preferences,
                        [key]: v === 0 ? -1 : v,
                      },
                    });
                  }}
                />
              </label>
            );
          })}
        </div>
        <LimitsEditor contract={contract} onChange={setContract} methods={methods} />
        <label className="aidomme-check">
          <input
            type="checkbox"
            checked={contract.games}
            disabled={contract.pacing !== "Domme"}
            onChange={(e) => patch({ games: e.target.checked })}
          />
          Games{contract.pacing !== "Domme" && " (only when she sets the pace)"}
        </label>
        <label className="aidomme-check">
          <input
            type="checkbox"
            checked={contract.canChangeDuringSession}
            onChange={(e) =>
              patch({ canChangeDuringSession: e.target.checked })
            }
          />
          Allow changing these settings once we've started
        </label>
        <details>
          <summary>Advanced</summary>
          <AuthorityEditor contract={contract} onChange={setContract} />
          <label className="aidomme-check">
            <input
              type="checkbox"
              checked={contract.punishment}
              onChange={(e) => patch({ punishment: e.target.checked })}
            />
            Punishment session (harsher, and harder to earn)
          </label>
          <label className="aidomme-check">
            <input
              type="checkbox"
              checked={contract.debug}
              onChange={(e) =>
                patch({
                  debug: e.target.checked,
                  gamesOnly: e.target.checked && contract.gamesOnly,
                })
              }
            />
            Debug session (can be ended; doesn't count)
          </label>
          {contract.debug && (
            <label className="aidomme-check">
              <input
                type="checkbox"
                checked={contract.gamesOnly}
                disabled={!contract.games}
                onChange={(e) => patch({ gamesOnly: e.target.checked })}
              />
              Games only
            </label>
          )}
          <div className="aidomme-actions">
            <button
              type="button"
              disabled={state.busy}
              onClick={() =>
                void guidedClient.mutate("/homework", { contract })
              }
            >
              Generate homework
            </button>
            <button
              type="button"
              disabled={state.busy}
              onClick={() =>
                void guidedClient.mutate("/homework", {
                  contract,
                  punishment: true,
                })
              }
            >
              Punishment homework
            </button>
          </div>
        </details>
        <div className="aidomme-actions">
          <button
            type="button"
            className="is-primary"
            disabled={state.busy || !!state.pending || (questions && !contract.debug)}
            title={questions && !contract.debug ? "Answer her first." : undefined}
            onClick={() => void start()}
          >
            Begin
          </button>
        </div>
      </section>

      <section className="aidomme-card">
        <h2>Promises and debts</h2>
        <LedgerPanel ledger={setup.ledger} />
        <PromiseForm />
      </section>

      {setup.messages?.length > 0 && (
        <section className="aidomme-card">
          <h3>Last time</h3>
          <ChatLog messages={setup.messages.slice(-12)} className="is-compact" />
        </section>
      )}
    </>
  );
}

function fresh(preset: Contract): Contract {
  return {
    ...preset,
    preferences: { intensity: -1, length: -1, denial: -1, mean: -1 },
    limits: {
      toys: [],
      anal: false,
      bondage: false,
      cei: false,
      selfOral: false,
      allowedMethodIds: [],
    },
    punishment: false,
    debug: false,
    gamesOnly: false,
    canChangeDuringSession: false,
  };
}

function LimitsEditor({
  contract,
  onChange,
  methods,
}: {
  contract: Contract;
  onChange: (c: Contract) => void;
  methods: any[];
}) {
  const limits = contract.limits;
  const set = (data: Partial<Contract["limits"]>) =>
    onChange({ ...contract, limits: { ...limits, ...data } });
  const toggleToy = (toy: string, on: boolean) =>
    set({
      toys: on ? [...limits.toys, toy] : limits.toys.filter((t) => t !== toy),
    });
  const eligible = useMemo(
    () =>
      methods.filter(
        (m) => m.toyCategory === "None" || limits.toys.includes(m.toyCategory),
      ),
    [methods, limits.toys],
  );
  return (
    <>
      <h3 style={{ marginTop: "0.8rem" }}>Toys she may use</h3>
      <div className="aidomme-chips">
        {TOYS.map(([toy, label]) => (
          <label className="aidomme-check" key={toy}>
            <input
              type="checkbox"
              checked={limits.toys.includes(toy)}
              onChange={(e) => toggleToy(toy, e.target.checked)}
            />
            {label}
          </label>
        ))}
      </div>
      <h3 style={{ marginTop: "0.6rem" }}>Also allowed</h3>
      <div className="aidomme-chips">
        {(
          [
            ["anal", "Anal"],
            ["bondage", "Bondage"],
            ["cei", "CEI"],
            ["selfOral", "Self-oral"],
          ] as const
        ).map(([key, label]) => (
          <label className="aidomme-check" key={key}>
            <input
              type="checkbox"
              checked={limits[key]}
              onChange={(e) => set({ [key]: e.target.checked })}
            />
            {label}
          </label>
        ))}
      </div>
      {contract.methods === "Domme" && eligible.length > 0 && (
        <details>
          <summary>
            Methods she may choose (
            {limits.allowedMethodIds.length || "all"})
          </summary>
          <div className="aidomme-method-choices">
            {eligible.map((method) => (
              <label className="aidomme-check" key={method.id}>
                <input
                  type="checkbox"
                  checked={limits.allowedMethodIds.includes(method.id)}
                  onChange={(e) =>
                    set({
                      allowedMethodIds: e.target.checked
                        ? [...limits.allowedMethodIds, method.id]
                        : limits.allowedMethodIds.filter(
                            (id) => id !== method.id,
                          ),
                    })
                  }
                />
                {method.name}
              </label>
            ))}
          </div>
          <small className="aidomme-muted">
            Leave all unchecked to let her use every method your toys allow.
          </small>
        </details>
      )}
    </>
  );
}

function AuthorityEditor({
  contract,
  onChange,
}: {
  contract: Contract;
  onChange: (c: Contract) => void;
}) {
  const concerns: [keyof Contract, string][] = [
    ["content", "What you watch"],
    ["methods", "How you touch yourself"],
    ["pacing", "Pace and rests"],
    ["edges", "Edges and holds"],
  ];
  return (
    <div className="aidomme-authority">
      {concerns.map(([key, label]) => (
        <label key={key}>
          {label}
          <select
            value={contract[key] as string}
            onChange={(e) => {
              const next = { ...contract, [key]: e.target.value } as Contract;
              if (next.pacing !== "Domme") next.games = false;
              onChange(next);
            }}
          >
            <option value="Domme">She decides</option>
            <option value="User">You decide</option>
          </select>
        </label>
      ))}
      <small className="aidomme-muted" style={{ gridColumn: "1 / -1" }}>
        She always decides whether, and when, you cum.
      </small>
    </div>
  );
}

// ─────────────────────────── Running session ───────────────────────────
function SessionScreen({ session }: { session: any }) {
  const state = useGuidedSession();
  const text = session.currentDirective?.text;
  const blocked = state.busy || !!state.pending || session.isPaused;
  const [editing, setEditing] = useState(false);
  return (
    <>
      <MediaWindow session={session} />
      <FollowUps />
      <section className="aidomme-card">
        <SessionStatus session={session} />
        <Obligations session={session} />
        {text && <p className="aidomme-current-command">{text}</p>}
        <OfferCard session={session} />
        <GameControls
          game={session.game}
          disabled={blocked}
          submit={(answer) => void guidedClient.control("game-answer", answer)}
        />
        <SessionActions session={session} withTracking />
        <PendingRequest />
      </section>
      <section className="aidomme-card">
        <ChatLog messages={state.snapshot.messages || []} hide={text} />
      </section>
      <section className="aidomme-card">
        <details>
          <summary>More</summary>
          <GuidedMenu />
          <p className="aidomme-muted">Report something she should know about:</p>
          <div className="aidomme-actions">
            <button
              disabled={blocked}
              onClick={() =>
                void guidedClient.mutate("/session/infraction", {
                  type: "Disobedience",
                })
              }
            >
              I disobeyed
            </button>
            <button
              disabled={blocked}
              onClick={() =>
                void guidedClient.mutate("/session/infraction", {
                  type: "RuinedOrgasm",
                })
              }
            >
              I ruined it
            </button>
          </div>
          {session.contract.canChangeDuringSession ? (
            <>
              <button onClick={() => setEditing(!editing)}>
                {editing ? "Close settings" : "Change session settings"}
              </button>
              {editing && (
                <ContractChange
                  session={session}
                  done={() => setEditing(false)}
                />
              )}
            </>
          ) : (
            <p className="aidomme-muted">
              Session settings were locked when you began.
            </p>
          )}
        </details>
      </section>
    </>
  );
}

function ContractChange({ session, done }: { session: any; done: () => void }) {
  const state = useGuidedSession();
  const [draft, setDraft] = useState<Contract>(session.contract);
  const [methods, setMethods] = useState<any[]>([]);
  useEffect(() => {
    guidedApi("/configuration")
      .then((data) => setMethods(data.methods.map((item: any) => item.value)))
      .catch(() => {});
  }, []);
  return (
    <div>
      <AuthorityEditor contract={draft} onChange={setDraft} />
      <LimitsEditor contract={draft} onChange={setDraft} methods={methods} />
      <label className="aidomme-check">
        <input
          type="checkbox"
          checked={draft.games}
          disabled={draft.pacing !== "Domme"}
          onChange={(e) => setDraft({ ...draft, games: e.target.checked })}
        />
        Games
      </label>
      <div className="aidomme-actions">
        <button
          className="is-primary"
          disabled={state.busy || !!state.pending}
          onClick={async () => {
            const response = await guidedClient.mutate(
              "/session/contract",
              {
                sessionId: session.sessionId,
                revision: session.contractRevision,
                contract: draft,
              },
              "PUT",
            );
            if (response) done();
          }}
        >
          Save
        </button>
      </div>
    </div>
  );
}

/** What she chose for you to look at, or a note that the content is yours to choose. */
function MediaWindow({ session }: { session: any }) {
  const state = useGuidedSession();
  if (session.contract.content !== "Domme")
    return (
      <div className="aidomme-media is-note">
        <span>
          You choose what to watch. Browse Cove; the companion comes with you
          and your edges are credited to what's on screen.
        </span>
        <button type="button" onClick={() => navigateToUrl("/videos")}>
          Browse videos
        </button>
      </div>
    );
  const media = state.media;
  if (!media)
    return (
      <div className="aidomme-media is-note">
        {state.mediaStatus || "She's choosing something for you…"}
      </div>
    );
  if (media.kind === "video")
    return (
      <div className="aidomme-media">
        <VideoPlayer
          key={media.id}
          streamUrl={`/api/stream/video/${media.id}`}
          format={media.format ?? "mp4"}
          videoCodec={media.videoCodec}
          audioCodec={media.audioCodec}
          duration={media.duration ?? 0}
          videoId={media.id}
          autostart
          showAbLoop={false}
          trackingEnabled={false}
          extensionSurface="detail"
          keyboardShortcutsEnabled={false}
        />
      </div>
    );
  if (media.kind === "image")
    return (
      <div className="aidomme-media">
        <img src={`/api/stream/image/${media.id}`} alt={media.title || ""} />
      </div>
    );
  return (
    <div className="aidomme-media">
      <audio key={media.id} src={`/api/audios/${media.id}/stream`} autoPlay controls />
    </div>
  );
}

// ═══════════════════════════ SETTINGS PANEL ═══════════════════════════
export function AiDomeSettingsPanel() {
  const [settings, setSettings] = useState<any | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    guidedApi("/settings")
      .then(setSettings)
      .catch((e) => setStatus(e?.message || "Could not load settings."));
  }, []);

  const save = async () => {
    if (!settings) return;
    setBusy(true);
    setStatus(null);
    try {
      setSettings(await guidedApi("/settings", settings, "PUT"));
      setStatus("Saved.");
    } catch (e: any) {
      setStatus(e?.message || "Could not save.");
    } finally {
      setBusy(false);
    }
  };

  const testVoice = async () => {
    setBusy(true);
    setStatus("Testing…");
    try {
      const res = await extensionFetch(`${API_BASE}/tts`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: "AI Domme voice test. Connection confirmed." }),
      });
      if (!res.ok) {
        setStatus(
          res.status === 503
            ? "TTS server unreachable. Check the URL."
            : `TTS test failed (${res.status}).`,
        );
        return;
      }
      const url = URL.createObjectURL(await res.blob());
      const audio = new Audio(url);
      audio.onended = () => URL.revokeObjectURL(url);
      await audio.play().catch(() => {});
      setStatus("Voice reachable.");
    } catch (e: any) {
      setStatus(e?.message || "TTS test failed.");
    } finally {
      setBusy(false);
    }
  };

  if (!settings)
    return (
      <div className="aidomme-settings-panel">
        <p className="panel-status">{status || "Loading…"}</p>
      </div>
    );

  return (
    <div className="aidomme-settings-panel">
      <p className="panel-intro">
        Optional Orpheus-FastAPI text-to-speech for spoken instructions. Leave
        the URL blank to disable voice.
      </p>
      <Field label="TTS server URL">
        <TextInput
          value={settings.ttsBaseUrl ?? ""}
          onChange={(v: string) => setSettings({ ...settings, ttsBaseUrl: v })}
          placeholder="http://localhost:5005"
        />
      </Field>
      <Field label="Voice">
        <TextInput
          value={settings.ttsVoice ?? ""}
          onChange={(v: string) => setSettings({ ...settings, ttsVoice: v })}
          placeholder="mia"
        />
      </Field>
      <Field label="Request timeout (seconds)">
        <NumberInput
          value={settings.ttsTimeoutSeconds ?? 30}
          onChange={(v: number | undefined) =>
            setSettings({ ...settings, ttsTimeoutSeconds: v ?? 30 })
          }
          min={1}
        />
      </Field>
      <div className="panel-actions">
        <SaveButton loading={busy} onClick={save} />
        <button className="btn btn-ghost" disabled={busy} onClick={testVoice}>
          Test voice
        </button>
      </div>
      {status && <p className="panel-status">{status}</p>}
    </div>
  );
}

// ═══════════════════════════ TUNING PAGE ═══════════════════════════
export function TuningLabPage() {
  return (
    <div className="aidomme-shell">
      <div className="aidomme-page-header">
        <h1>AI Domme tuning</h1>
        <span className="aidomme-muted">
          Your methods, punishment endings and calibration
        </span>
      </div>
      <DefinitionSettings />
    </div>
  );
}

export default {
  components: {
    TrackerCompanion,
    CommandCenterPage,
    AiDomeSettingsPanel,
    TuningLabPage,
  },
};
