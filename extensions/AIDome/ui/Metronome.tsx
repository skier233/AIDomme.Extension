import React, { useCallback, useEffect, useRef, useState } from "react";
import { extensionFetch } from "@cove/runtime/api";
import { MetronomeAudio } from "./MetronomeAudio";

type Preferences = { bpm: number; volume: number; muted: boolean };
const defaults: Preferences = { bpm: 60, volume: 0.3, muted: false };
const PAUSE_METRONOME = "aidomme:pause-metronome";
export function pauseMetronome() {
  window.dispatchEvent(new Event(PAUSE_METRONOME));
}

async function lease(clientId: string, release = false) {
  const response = await extensionFetch(
    `/api/ext/aidome/tracker/metronome/${release ? "release" : "lease"}`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ clientId }),
    },
  );
  if (!response.ok)
    throw new Error(`Metronome connection failed (${response.status}).`);
  if (release) return false;
  if (
    !(response.headers.get("content-type") || "").includes("application/json")
  )
    throw new Error(
      "Reload Cove with the updated AI Domme backend to use the metronome.",
    );
  return (await response.json()).granted === true;
}

/** Lives in the app-wide companion even when its portal is hidden or closed. */
export function useMetronome(userId?: number) {
  const [preferences, setPreferences] = useState(defaults);
  const [running, setRunning] = useState(false);
  const [starting, setStarting] = useState(false);
  const [pulse, setPulse] = useState(false);
  const [error, setError] = useState("");
  const audio = useRef<MetronomeAudio | null>(null);
  const owner = useRef<string | null>(null);
  const epoch = useRef(0);
  const heartbeat = useRef<ReturnType<typeof setInterval> | null>(null);
  const frame = useRef(0);
  const key = userId ? `aidomme-metronome:${userId}` : null;

  const pause = useCallback(() => {
    epoch.current++;
    audio.current?.stop();
    if (heartbeat.current) clearInterval(heartbeat.current);
    heartbeat.current = null;
    cancelAnimationFrame(frame.current);
    setPulse(false);
    setRunning(false);
    setStarting(false);
    const client = owner.current;
    owner.current = null;
    if (client)
      void lease(client, true).catch(() => {
        /* Short lease expires without a response. */
      });
  }, []);

  useEffect(() => {
    pause();
    let saved = defaults;
    try {
      const stored = JSON.parse(localStorage.getItem(key || "") || "null");
      if (
        stored &&
        Number.isFinite(stored.bpm) &&
        Number.isFinite(stored.volume)
      )
        saved = {
          bpm: Math.max(20, Math.min(240, Math.round(stored.bpm))),
          volume: Math.max(0, Math.min(1, stored.volume)),
          muted: !!stored.muted,
        };
    } catch {
      /* Optional local preferences. */
    }
    setPreferences(saved);
    setError("");
    const engine = new MetronomeAudio((reason) => {
      pause();
      setError(reason);
    });
    audio.current = engine;
    engine.configure(saved.bpm, saved.volume, saved.muted);
    const leave = () => pause();
    const visibility = () => {
      if (document.hidden) {
        pause();
        setError("Metronome paused while this tab was away.");
      }
    };
    window.addEventListener("pagehide", leave);
    window.addEventListener(PAUSE_METRONOME, leave);
    document.addEventListener("visibilitychange", visibility);
    return () => {
      pause();
      engine.dispose();
      audio.current = null;
      window.removeEventListener("pagehide", leave);
      window.removeEventListener(PAUSE_METRONOME, leave);
      document.removeEventListener("visibilitychange", visibility);
    };
  }, [key, pause]);

  const change = (patch: Partial<Preferences>) => {
    const value = { ...preferences, ...patch };
    setPreferences(value);
    audio.current?.configure(value.bpm, value.volume, value.muted);
    if (key)
      try {
        localStorage.setItem(key, JSON.stringify(value));
      } catch {
        /* Optional preference. */
      }
  };

  const toggle = useCallback(async () => {
    if (owner.current) {
      pause();
      return;
    }
    const engine = audio.current;
    if (!userId || !engine) return;
    const client = crypto.randomUUID();
    owner.current = client;
    const revision = ++epoch.current;
    setStarting(true);
    setError("");
    // Unlock synchronously within this button/hotkey gesture, before network awaits.
    const unlocked = engine.unlock();
    try {
      await unlocked;
      if (revision !== epoch.current) return;
      const sent = performance.now();
      const granted = await lease(client);
      if (revision !== epoch.current) {
        if (granted) void lease(client, true).catch(() => {});
        return;
      }
      if (!granted)
        throw new Error(
          "Metronome is playing in another tab or device. Pause it there first.",
        );
      if (performance.now() >= sent + 4000)
        throw new Error("Connection was too slow. Press Start again.");
      engine.start(sent + 4000);
      setRunning(true);
      setStarting(false);
      let renewing = false;
      heartbeat.current = setInterval(async () => {
        if (renewing || revision !== epoch.current) return;
        renewing = true;
        const sent = performance.now();
        try {
          const granted = await lease(client);
          if (revision !== epoch.current) {
            if (granted) void lease(client, true).catch(() => {});
            return;
          }
          if (!granted || performance.now() >= sent + 4000)
            throw new Error("Metronome ownership lost. Press Start to resume.");
          engine.renew(sent + 4000);
        } catch (e) {
          if (revision === epoch.current) {
            pause();
            setError((e as Error).message);
          }
        } finally {
          renewing = false;
        }
      }, 1500);
      const animate = () => {
        if (revision !== epoch.current) return;
        setPulse(engine.pulse());
        frame.current = requestAnimationFrame(animate);
      };
      frame.current = requestAnimationFrame(animate);
    } catch (e) {
      if (revision === epoch.current) {
        pause();
        setError((e as Error).message);
      }
    }
  }, [pause, userId]);

  const controls = (
    <section className="aidomme-metronome" aria-label="Metronome">
      <div className="aidomme-metronome-heading">
        <strong>Metronome</strong>
        <span
          className={`aidomme-beat${pulse ? " is-beating" : ""}`}
          aria-hidden="true"
        />
        <small>
          {starting ? "Connecting…" : running ? "Playing" : "Paused"}
        </small>
      </div>
      <label>
        Tempo (BPM)
        <input
          aria-label="Tempo (BPM)"
          type="range"
          min={20}
          max={240}
          step={1}
          value={preferences.bpm}
          onChange={(e) => change({ bpm: Number(e.target.value) })}
        />
        <output>{preferences.bpm}</output>
      </label>
      <div className="aidomme-tracker-actions">
        <button disabled={!userId} onClick={() => void toggle()}>
          {starting || running ? "Pause metronome" : "Start metronome"}
        </button>
        <button
          aria-pressed={preferences.muted}
          onClick={() => change({ muted: !preferences.muted })}
        >
          {preferences.muted ? "Unmute" : "Mute"}
        </button>
      </div>
      <label>
        Volume
        <input
          aria-label="Metronome volume"
          type="range"
          min={0}
          max={100}
          value={Math.round(preferences.volume * 100)}
          onChange={(e) => change({ volume: Number(e.target.value) / 100 })}
        />
        <output>{Math.round(preferences.volume * 100)}%</output>
      </label>
      {error && <p role="alert">{error}</p>}
      <small>
        One beat per stroke. Hide/close keeps playing. Shortcut configured in
        Cove (default Alt+M). Leaving this tab pauses playback.
      </small>
    </section>
  );
  return { controls, toggle, running, pause };
}
