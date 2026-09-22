import React from "react";
import {
  Field,
  TextInput,
  NumberInput,
  SaveButton,
} from "@cove/runtime/components";
import { extensionFetch } from "@cove/runtime/api";
export { TrackerCompanion } from "./Tracker";
import { TrackerCompanion, openTracker } from "./Tracker";
import { DefinitionSettings } from "./Configuration";
import { guidedClient, useGuidedSession } from "./GuidedClient";
import { CommitmentPanel } from "./Commitments";
import {
  defaultPolicy,
  GuidedPanel,
  SessionPolicyEditor,
} from "./SessionControls";

const { useEffect, useRef, useState, useCallback } = React;

const API_BASE = "/api/ext/aidome";

// ─────────────────────────── API helpers ───────────────────────────
async function api(path: string, options: RequestInit = {}): Promise<any> {
  const response = await extensionFetch(`${API_BASE}${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
  });
  const text = await response.text();
  let data: any = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }
  if (!response.ok) {
    const detail = data?.detail || data?.message || text || response.statusText;
    throw new Error(detail || "Request failed.");
  }
  return data;
}

const post = (path: string, body?: any) =>
  api(path, {
    method: "POST",
    body: body === undefined ? undefined : JSON.stringify(body),
  });

// ─────────────────────────── formatting ───────────────────────────
function formatTime(totalSeconds: number): string {
  const t = Math.max(0, Math.floor(totalSeconds || 0));
  const m = Math.floor(t / 60);
  const s = t % 60;
  return `${m}:${s.toString().padStart(2, "0")}`;
}
const formatParam = (v: number) => (v === -1 ? "Auto" : String(v));
const senderClass = (sender: string) =>
  sender === "Domme"
    ? "msg-domme"
    : sender === "System"
      ? "msg-system"
      : sender === "Manager"
        ? "msg-manager"
        : "";
const localTime = (iso: string) => {
  try {
    return new Date(iso).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return "";
  }
};
const localDateTime = (iso: string) => {
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return "";
  }
};

// ─────────────────────────── default params ───────────────────────────
function defaultParams() {
  return {
    policy: { ...defaultPolicy, allowedMethodIds: [] as number[] },
    intensity: -1,
    length: -1,
    denial: -1,
    kink: -1,
    mean: -1,
    onlyGamesMode: false,
    allowToys: false,
    allowVibrator: false,
    allowFleshlight: false,
    allowAutoblow: false,
    allowProstate: false,
    allowAnal: false,
    allowBondage: false,
    allowCEI: false,
    allowSelfOral: false,
    isDebugSession: false,
  };
}

// ═══════════════════════════ COMMAND CENTER PAGE ═══════════════════════════
export function CommandCenterPage() {
  const state = useGuidedSession();
  const { snapshot, setup, error } = state;
  const [pending, setPending] = useState(defaultParams());
  const [confessionDismissed, setConfessionDismissed] = useState(false);
  const chatRef = useRef<HTMLDivElement | null>(null);
  const sessionActive = !!snapshot?.sessionActive;
  const online = snapshot?.online;
  const messages: any[] = snapshot?.messages ?? setup?.messages ?? [];
  const startSession = async (punishment: boolean) => {
    const response = await guidedClient.mutate(
      punishment ? "/session/start-punishment" : "/session/start",
      pending,
    );
    if (response) openTracker();
  };
  const generateHomework = async (punishment: boolean) => {
    await guidedClient.mutate(
      punishment ? "/punishment-homework" : "/homework",
      pending,
    );
  };
  const handleConfession = async (confessed: boolean) => {
    const result = await guidedClient.mutate("/confession", { confessed });
    if (result) setConfessionDismissed(true);
  };
  useEffect(() => {
    if (chatRef.current)
      chatRef.current.scrollTop = chatRef.current.scrollHeight;
  }, [messages.length]);
  const setP = (patch: Partial<ReturnType<typeof defaultParams>>) =>
    setPending((p) => ({ ...p, ...patch }));

  // ─────────────── render ───────────────
  return (
    <div className="aidome-shell">
      <div className="aidome-header">
        <div>
          <h1 className="brand-title">AI Domme</h1>
          <button type="button" onClick={openTracker}>
            Open tracker
          </button>
          <p className="brand-sub">
            {sessionActive
              ? "Live session in progress"
              : "Session configuration"}
          </p>
        </div>
      </div>
      {error && <div className="aidome-error">{error}</div>}
      {sessionActive ? renderSession() : renderSetup()}
      <CommitmentPanel />
      {!sessionActive && (
        <details>
          <summary>Methods and tuning</summary>
          <DefinitionSettings />
        </details>
      )}
    </div>
  );

  function renderSetup() {
    const suspicion = !confessionDismissed ? setup?.suspicion : null;
    const punishments: any[] = setup?.activePunishments ?? [];
    return (
      <div className="setup-screen">
        <div className="setup-card">
          <div className="setup-section">
            <h3 className="section-title">Parameters</h3>
            <p className="section-hint">
              Set to -1 for auto (based on history)
            </p>
            <div className="slider-group">
              {(["intensity", "length", "denial", "kink", "mean"] as const).map(
                (key) => (
                  <div className="slider-row" key={key}>
                    <label>
                      <span style={{ textTransform: "capitalize" }}>{key}</span>
                      <span className="slider-val">
                        {formatParam((pending as any)[key])}
                      </span>
                    </label>
                    <input
                      type="range"
                      min={-1}
                      max={10}
                      value={(pending as any)[key]}
                      onChange={(e) =>
                        setP({ [key]: Number(e.target.value) } as any)
                      }
                    />
                  </div>
                ),
              )}
            </div>
          </div>

          <div className="setup-section">
            <h3 className="section-title">Toys</h3>
            <div className="toggle-grid">
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.allowToys}
                  onChange={(e) => setP({ allowToys: e.target.checked })}
                />
                <span>Allow Toys</span>
              </label>
              {pending.allowToys && (
                <>
                  <label className="toggle-item sub">
                    <input
                      type="checkbox"
                      checked={pending.allowVibrator}
                      onChange={(e) =>
                        setP({ allowVibrator: e.target.checked })
                      }
                    />
                    <span>Vibrator</span>
                  </label>
                  <label className="toggle-item sub">
                    <input
                      type="checkbox"
                      checked={pending.allowFleshlight}
                      onChange={(e) =>
                        setP({ allowFleshlight: e.target.checked })
                      }
                    />
                    <span>Fleshlight</span>
                  </label>
                  <label className="toggle-item sub">
                    <input
                      type="checkbox"
                      checked={pending.allowAutoblow}
                      onChange={(e) =>
                        setP({ allowAutoblow: e.target.checked })
                      }
                    />
                    <span>Autoblow</span>
                  </label>
                  <label className="toggle-item sub">
                    <input
                      type="checkbox"
                      checked={pending.allowProstate}
                      onChange={(e) =>
                        setP({ allowProstate: e.target.checked })
                      }
                    />
                    <span>Prostate</span>
                  </label>
                </>
              )}
            </div>
          </div>

          <div className="setup-section">
            <h3 className="section-title">Kinks</h3>
            <div className="toggle-grid">
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.allowAnal}
                  onChange={(e) => setP({ allowAnal: e.target.checked })}
                />
                <span>Anal</span>
              </label>
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.allowBondage}
                  onChange={(e) => setP({ allowBondage: e.target.checked })}
                />
                <span>Bondage</span>
              </label>
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.allowCEI}
                  onChange={(e) => setP({ allowCEI: e.target.checked })}
                />
                <span>CEI</span>
              </label>
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.allowSelfOral}
                  onChange={(e) => setP({ allowSelfOral: e.target.checked })}
                />
                <span>Self-Oral</span>
              </label>
            </div>
          </div>

          <SessionPolicyEditor
            value={pending.policy}
            onChange={(policy) => setP({ policy })}
            disabled={state.busy}
          />
          <div className="setup-actions">
            <button
              className="btn btn-primary"
              disabled={state.busy || !!state.pending}
              onClick={() => startSession(false)}
            >
              Start Session
            </button>
            <button
              className="btn btn-danger"
              disabled={state.busy || !!state.pending}
              onClick={() => startSession(true)}
            >
              Punishment Session
            </button>
          </div>

          <div className="setup-section">
            <div className="toggle-grid">
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.isDebugSession}
                  onChange={(e) => setP({ isDebugSession: e.target.checked })}
                />
                <span>Debug Session</span>
              </label>
              <label className="toggle-item">
                <input
                  type="checkbox"
                  checked={pending.onlyGamesMode}
                  onChange={(e) => setP({ onlyGamesMode: e.target.checked })}
                />
                <span>Only Games Mode</span>
              </label>
            </div>
          </div>

          <div className="setup-debug">
            <button
              className="btn btn-ghost"
              onClick={() => generateHomework(false)}
            >
              Generate Homework
            </button>
            <button
              className="btn btn-ghost"
              onClick={() => generateHomework(true)}
            >
              Punishment Homework
            </button>
          </div>
        </div>

        {suspicion && (
          <div className="setup-card confession-card">
            <h3 className="section-title">Before We Begin...</h3>
            <p className="confession-text">{suspicion.message}</p>
            <p className="confession-date">
              Last session: {localDateTime(suspicion.sessionDate)}
            </p>
            <div className="setup-actions">
              <button
                className="btn btn-danger"
                onClick={() => handleConfession(true)}
              >
                Yes, I came without permission
              </button>
              <button
                className="btn btn-primary"
                onClick={() => handleConfession(false)}
              >
                No, I didn't
              </button>
            </div>
          </div>
        )}

        {punishments.length > 0 && (
          <div className="setup-card">
            <h3 className="section-title">Active Punishments</h3>
            <div className="punishment-list">
              {punishments.map((p) => (
                <div className="punishment-item" key={p.id}>
                  <span className="punishment-desc">{p.description}</span>
                  {p.denialEndsAtUtc && (
                    <span className="punishment-meta">
                      Ends: {localDateTime(p.denialEndsAtUtc)}
                    </span>
                  )}
                  {p.remainingCount > 0 && (
                    <span className="punishment-meta">
                      Remaining: {p.remainingCount}
                    </span>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {messages.length > 0 && (
          <div className="setup-card last-messages">
            <h3 className="section-title">Recent Messages</h3>
            <div className="chat-window compact" ref={chatRef}>
              {messages.slice(-20).map((m) => (
                <div
                  className={`chat-line ${senderClass(m.sender)}`}
                  key={m.id}
                >
                  <span className="ts">{localTime(m.timestamp)}</span>
                  <span className="sender">{m.sender}</span>
                  <span className="body">{m.content}</span>
                </div>
              ))}
            </div>
          </div>
        )}
      </div>
    );
  }

  function renderSession() {
    return (
      <div className="session-screen">
        <div className="stats-bar">
          <div className="stat">
            <span className="stat-label">Step</span>
            <span className="stat-value">{online?.stepNumber || 0}</span>
          </div>
          <div className="stat">
            <span className="stat-label">Edges</span>
            <span className="stat-value">{online?.edgesThisSession || 0}</span>
          </div>
          <div className="stat">
            <span className="stat-label">Points</span>
            <span className="stat-value">{online?.points || 0}</span>
          </div>
          <div className="stat">
            <span className="stat-label">Time</span>
            <span className="stat-value">
              {formatTime(online?.elapsedSeconds || 0)}
            </span>
          </div>
        </div>
        <GuidedPanel />
      </div>
    );
  }
}
// ═══════════════════════════ SETTINGS PANEL ═══════════════════════════
export function AiDomeSettingsPanel() {
  const [settings, setSettings] = useState<any | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api("/settings")
      .then(setSettings)
      .catch((e) => setStatus(e?.message || "Could not load settings."));
  }, []);

  const save = async () => {
    if (!settings) return;
    setBusy(true);
    setStatus(null);
    try {
      const saved = await api("/settings", {
        method: "PUT",
        body: JSON.stringify(settings),
      });
      setSettings(saved);
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
        body: JSON.stringify({
          text: "AI Domme voice test. Connection confirmed.",
        }),
      });
      if (!res.ok) {
        setStatus(
          res.status === 503
            ? "TTS server unreachable — check the URL."
            : `TTS test failed (${res.status}).`,
        );
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
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

  if (!settings) {
    return (
      <div className="aidome-settings-panel">
        <p className="panel-status">{status || "Loading…"}</p>
      </div>
    );
  }

  return (
    <div className="aidome-settings-panel">
      <p className="panel-intro">
        Optional Orpheus-FastAPI text-to-speech for spoken directives. Leave the
        URL blank to disable voice.
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

// ═══════════════════════════ TUNING LAB PAGE ═══════════════════════════
export function TuningLabPage() {
  return (
    <div className="aidome-shell">
      <div className="aidome-header">
        <div>
          <h1 className="brand-title">Tuning Lab</h1>
          <p className="brand-sub">Personal methods and calibration</p>
        </div>
      </div>
      <div className="setup-screen">
        <div className="setup-card">
          <p className="section-hint">
            Edit your method and punishment definitions, restore shared
            defaults, or optimize method multipliers using preserved
            calibration.
          </p>
        </div>
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
