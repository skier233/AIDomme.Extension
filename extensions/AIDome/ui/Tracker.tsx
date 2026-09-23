import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import { useQueryClient } from "@tanstack/react-query";
import { extensionFetch } from "@cove/runtime/api";
import { useMetronome } from "./Metronome";
import { guidedClient, useGuidedSession } from "./GuidedClient";
import { GuidedMenu, GuidedPanel } from "./SessionControls";
import {
  getActiveMedia,
  useActiveMedia,
  useExtensionKeyboardBindings,
  useRegisterExtensionKeyboardActions,
} from "@cove/runtime/components";

const ID = "cove.community.aidome";
const OPEN_TRACKER = "aidomme:open-tracker";
export function openTracker() {
  window.dispatchEvent(new Event(OPEN_TRACKER));
}
type Preferences = {
  userId: number;
  autoLike: boolean;
  edgeWeight: number;
  orgasmWeight: number;
  settingId: string;
};
type Event = {
  id: string;
  kind: string;
  mediaKind?: string;
  mediaId?: number;
  likeWeight: number;
  receivedAtUtc: string;
};
type Snapshot = {
  preferences: Preferences;
  edges: number;
  orgasms: number;
  edgesSinceLastOrgasm: number;
  lastOrgasmAtUtc?: string;
  recent: Event[];
};
type Request = {
  id: string;
  settingId: string;
  kind: string;
  occurredAtUtc: string;
  source: string;
  mediaKind?: string;
  mediaId?: number;
  positionSeconds?: number;
  guidedSessionId?: number;
  guidedStepNumber?: number;
};

async function api(path: string, method = "GET", body?: unknown) {
  const response = await extensionFetch(`/api/ext/aidome/tracker${path}`, {
    method,
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (/^\s*</.test(text))
    throw new Error(
      "Cove returned a web page instead of tracker data. Check that the updated AI Domme backend is loaded, then reload Cove.",
    );
  let data;
  try {
    data = JSON.parse(text);
  } catch {
    throw new Error(
      `Tracker returned an invalid response (${response.status}).`,
    );
  }
  if (!response.ok)
    throw new Error(
      data.message ||
        data.detail ||
        `Tracker request failed (${response.status}).`,
    );
  return data;
}

export function TrackerCompanion() {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [error, setError] = useState<string | null>(null);
  const [connectionError, setConnectionError] = useState<string | null>(null);
  const [retry, setRetry] = useState<Request | null>(null);
  const [hidden, setHidden] = useState(false);
  const [closed, setClosed] = useState(false);
  const [expanded, setExpanded] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [notice, setNotice] = useState("");
  const [position, setPosition] = useState<{ x: number; y: number } | null>(
    null,
  );
  const [portal, setPortal] = useState<Element | null>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const media = useActiveMedia();
  const bindings = useExtensionKeyboardBindings(ID);
  const queryClient = useQueryClient();
  const guided = useGuidedSession();
  const session = guided.snapshot?.session;
  // When the domme sets the pace, the metronome follows her tempo.
  const metronome = useMetronome(
    snapshot?.preferences.userId,
    session?.contract?.pacing === "Domme" && !session.isPaused
      ? session.tempo
      : session?.contract?.pacing === "Domme"
        ? 0
        : undefined,
  );
  useEffect(() => {
    if (snapshot?.preferences.userId)
      return guidedClient.start(snapshot.preferences.userId);
  }, [snapshot?.preferences.userId]);
  const mounted = useRef(true);
  const requestRevision = useRef(0);
  const refreshSerial = useRef(0);
  const uiKey = snapshot
    ? `aidomme-tracker-ui:${snapshot.preferences.userId}`
    : null;

  const refresh = useCallback(async () => {
    const revision = requestRevision.current;
    const serial = ++refreshSerial.current;
    try {
      let data = await api("/state");
      if (
        data.preferences.settingId === "00000000-0000-0000-0000-000000000000"
      ) {
        await api("/initialize", "POST");
        data = await api("/state");
      }
      if (
        mounted.current &&
        revision === requestRevision.current &&
        serial === refreshSerial.current &&
        !busyRef.current
      ) {
        setSnapshot(data);
        setConnectionError(null);
      }
    } catch (e) {
      if (
        mounted.current &&
        revision === requestRevision.current &&
        serial === refreshSerial.current
      )
        setConnectionError((e as Error).message);
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    void refresh();
    const timer = setInterval(() => {
      if (!busyRef.current) void refresh();
    }, 5000);
    const onFocus = () => void refresh();
    window.addEventListener("focus", onFocus);
    return () => {
      mounted.current = false;
      clearInterval(timer);
      window.removeEventListener("focus", onFocus);
    };
  }, [refresh]);

  useEffect(() => {
    if (!uiKey) return;
    try {
      const saved = JSON.parse(localStorage.getItem(uiKey) || "null");
      if (saved) {
        setHidden(!!saved.hidden);
        setClosed(!!saved.closed);
        setExpanded(!!saved.expanded);
        if (
          Number.isFinite(saved.position?.x) &&
          Number.isFinite(saved.position?.y)
        )
          setPosition({
            x: Math.max(
              8,
              Math.min(
                saved.position.x,
                window.innerWidth - (panelRef.current?.offsetWidth || 320) - 8,
              ),
            ),
            y: Math.max(
              8,
              Math.min(
                saved.position.y,
                window.innerHeight - (panelRef.current?.offsetHeight || 80) - 8,
              ),
            ),
          });
      }
    } catch {
      /* Defaults are usable when storage is unavailable. */
    }
  }, [uiKey]);
  const saveUi = useCallback(
    (value: object) => {
      if (!uiKey) return;
      try {
        localStorage.setItem(
          uiKey,
          JSON.stringify({ hidden, closed, expanded, position, ...value }),
        );
      } catch {
        /* Optional preference. */
      }
    },
    [uiKey, hidden, closed, expanded, position],
  );
  const toggle = useCallback(() => {
    if (closed) {
      setClosed(false);
      setHidden(false);
      saveUi({ closed: false, hidden: false });
      return;
    }
    setHidden((value) => {
      saveUi({ hidden: !value });
      return !value;
    });
  }, [saveUi, closed]);

  useEffect(() => {
    const open = () => {
      setClosed(false);
      setHidden(false);
      saveUi({ closed: false, hidden: false });
    };
    window.addEventListener(OPEN_TRACKER, open);
    return () => window.removeEventListener(OPEN_TRACKER, open);
  }, [saveUi]);

  useEffect(() => {
    const update = () => setPortal(document.fullscreenElement);
    document.addEventListener("fullscreenchange", update);
    update();
    return () => document.removeEventListener("fullscreenchange", update);
  }, []);
  const clamp = useCallback(
    (x: number, y: number) => ({
      x: Math.max(
        8,
        Math.min(
          x,
          window.innerWidth - (panelRef.current?.offsetWidth || 320) - 8,
        ),
      ),
      y: Math.max(
        8,
        Math.min(
          y,
          window.innerHeight - (panelRef.current?.offsetHeight || 80) - 8,
        ),
      ),
    }),
    [],
  );
  useEffect(() => {
    const resize = () =>
      setPosition((old) => (old ? clamp(old.x, old.y) : old));
    window.addEventListener("resize", resize);
    resize();
    return () => window.removeEventListener("resize", resize);
  }, [clamp, expanded, settingsOpen, hidden]);

  const invalidateMedia = useCallback(
    (event: Event) => {
      if (!event.mediaId || !event.mediaKind) return;
      // Refresh host counters, history, list cards, and gallery aggregates after weighted effects/undo.
      void queryClient.invalidateQueries({
        predicate: (query) => {
          const key = query.queryKey;
          return (
            key[0] === "engagement" ||
            key[0] === event.mediaKind ||
            key[0] === `${event.mediaKind}s` ||
            key[0] === "gallery-like-count"
          );
        },
      });
    },
    [queryClient],
  );
  const mutate = useCallback(
    async (path: string, method = "POST", body?: unknown) => {
      if (busyRef.current) return false;
      busyRef.current = true;
      setBusy(true);
      setError(null);
      requestRevision.current++;
      try {
        const result = await api(path, method, body);
        if (!mounted.current) return true;
        if (result.snapshot) {
          setSnapshot(result.snapshot);
          invalidateMedia(result.event);
        } else
          setSnapshot((old) => (old ? { ...old, preferences: result } : old));
        void guidedClient.refresh();
        return true;
      } catch (e) {
        if (mounted.current) setError((e as Error).message);
        return false;
      } finally {
        busyRef.current = false;
        if (mounted.current) setBusy(false);
      }
    },
    [invalidateMedia],
  );

  const record = useCallback(
    async (kind: "edge" | "orgasm", source = "button", previous?: Request) => {
      if (!snapshot || busyRef.current || (retry && !previous)) return;
      if (
        snapshot.preferences.settingId ===
        "00000000-0000-0000-0000-000000000000"
      ) {
        if (await mutate("/setting"))
          setNotice("Tracker ready. Press again to record.");
        return;
      }
      const current = getActiveMedia(); // Capture at invocation, never after awaiting a fetch.
      const guidance = guidedClient.getSnapshot().snapshot;
      const body = previous || {
        id: crypto.randomUUID(),
        settingId: snapshot.preferences.settingId,
        kind,
        source,
        occurredAtUtc: new Date().toISOString(),
        mediaKind: current?.kind,
        mediaId: current?.id,
        positionSeconds: current?.positionSeconds,
        guidedSessionId: guidance?.session ? guidance.session.sessionId : 0,
        guidedStepNumber: guidance?.session
          ? guidance.session.stepNumber
          : undefined,
      };
      const success = await mutate("/events", "POST", body);
      if (success) {
        setRetry(null);
        setNotice(
          `${kind === "edge" ? "Edge" : "Orgasm"} recorded${body.mediaId ? ` on ${body.mediaKind} #${body.mediaId}` : " without media"}.`,
        );
      } else setRetry(body); // Retry reuses the ID and captured context; it is not another event.
    },
    [snapshot, retry, mutate],
  );
  useEffect(() => {
    const changed = (event: globalThis.Event) => {
      invalidateMedia((event as CustomEvent).detail);
      void refresh();
    };
    window.addEventListener("aidomme:tracker-changed", changed);
    return () => window.removeEventListener("aidomme:tracker-changed", changed);
  }, [invalidateMedia, refresh]);

  const actions = useMemo(
    () => [
      // The companion is an app-wide overlay. This declared surface allows shortcuts through
      // keyboard-owning media lightboxes; Cove still suppresses editable targets and key repeat.
      {
        id: "tracker-edge",
        surface: "overlay" as const,
        action: () => {
          void record("edge", "hotkey");
        },
      },
      {
        id: "tracker-orgasm",
        surface: "overlay" as const,
        action: () => {
          void record("orgasm", "hotkey");
        },
      },
      { id: "tracker-toggle", surface: "overlay" as const, action: toggle },
      {
        id: "metronome-toggle",
        surface: "overlay" as const,
        action: () => {
          void metronome.toggle();
        },
      },
      {
        id: "session-complete",
        surface: "overlay" as const,
        action: () => {
          void guidedClient.control("done");
        },
      },
      {
        id: "session-permission",
        surface: "overlay" as const,
        action: () => {
          void guidedClient.control("request-permission");
        },
      },
      {
        id: "session-stop-request",
        surface: "overlay" as const,
        action: () => {
          void guidedClient.control("request-rest");
        },
      },
      {
        id: "offer-accept",
        surface: "overlay" as const,
        action: () => {
          if (guidedClient.getSnapshot().snapshot?.session?.offer)
            void guidedClient.control("offer-accept");
        },
      },
      {
        id: "offer-decline",
        surface: "overlay" as const,
        action: () => {
          if (guidedClient.getSnapshot().snapshot?.session?.offer)
            void guidedClient.control("offer-decline");
        },
      },
      {
        id: "session-pause",
        surface: "overlay" as const,
        action: () => {
          void guidedClient.control(
            guidedClient.getSnapshot().snapshot?.session?.isPaused
              ? "resume"
              : "pause",
          );
        },
      },
    ],
    [record, toggle, metronome.toggle],
  );
  useRegisterExtensionKeyboardActions(ID, actions);

  const drag = useRef<{
    x: number;
    y: number;
    left: number;
    top: number;
  } | null>(null);
  const beginDrag = (e: React.PointerEvent) => {
    if ((e.target as HTMLElement).closest("button") || e.button !== 0) return;
    const box = panelRef.current!.getBoundingClientRect();
    drag.current = { x: e.clientX, y: e.clientY, left: box.left, top: box.top };
    e.currentTarget.setPointerCapture(e.pointerId);
  };
  const moveDrag = (e: React.PointerEvent) => {
    if (drag.current)
      setPosition(
        clamp(
          drag.current.left + e.clientX - drag.current.x,
          drag.current.top + e.clientY - drag.current.y,
        ),
      );
  };
  const endDrag = () => {
    drag.current = null;
    saveUi({ position });
  };
  const settings = snapshot?.preferences;
  const shortcuts = (id: string) => (bindings[id] || []).join(" / ");
  const content = (
    <div
      className={`aidomme-tracker${hidden ? " is-hidden" : ""}`}
      ref={panelRef}
      style={
        position
          ? { left: position.x, top: position.y }
          : { right: 16, bottom: 16 }
      }
    >
      {hidden ? (
        <button
          onClick={toggle}
          title={`Show tracker (${shortcuts("tracker-toggle")})`}
        >
          AI Domme tracker
        </button>
      ) : (
        <>
          <header
            onPointerDown={beginDrag}
            onPointerMove={moveDrag}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
          >
            <strong>AI Domme</strong>
            <button
              onClick={() => {
                setExpanded(!expanded);
                saveUi({ expanded: !expanded });
              }}
              aria-expanded={expanded}
              aria-label="More options"
              title="More options"
            >
              ⋯
            </button>
            <button onClick={toggle} aria-label="Hide tracker" title="Hide">
              –
            </button>
            <button
              onClick={() => {
                setClosed(true);
                saveUi({ closed: true });
              }}
              aria-label="Close tracker"
              title={`Close tracker · reopen with ${shortcuts("tracker-toggle")} or from AI Domme`}
            >
              ×
            </button>
          </header>
          {!snapshot ? (
            <p>{error || connectionError || "Connecting…"}</p>
          ) : (
            <>
              <GuidedPanel />
              <div className="aidomme-tracker-actions">
                <button
                  disabled={busy || !!retry}
                  onClick={() => void record("edge")}
                  title={shortcuts("tracker-edge")}
                >
                  Edge
                </button>
                <button
                  disabled={busy || !!retry}
                  onClick={() => void record("orgasm")}
                  title={shortcuts("tracker-orgasm")}
                >
                  Orgasm
                </button>
                <button
                  className="aidomme-tracker-undo"
                  disabled={busy || !snapshot.recent.length || !!retry}
                  title="Undo the latest recorded event"
                  onClick={async () => {
                    if (await mutate(`/events/${snapshot.recent[0].id}/undo`))
                      setNotice("Latest event undone.");
                  }}
                >
                  Undo
                </button>
              </div>
              <small className="aidomme-tracker-counts">
                {snapshot.edges} edges · {snapshot.orgasms} orgasms tracked
              </small>
              {notice && <small role="status">{notice}</small>}
              {(error || connectionError) && (
                <p role="alert">{error || connectionError}</p>
              )}
              {retry && (
                <div className="aidomme-tracker-actions">
                  <button
                    disabled={busy}
                    onClick={() =>
                      void record(
                        retry.kind as "edge" | "orgasm",
                        retry.source,
                        retry,
                      )
                    }
                  >
                    Retry same event
                  </button>
                  <button
                    disabled={busy}
                    onClick={async () => {
                      setRetry(null);
                      await refresh();
                      setNotice(
                        "Request dismissed. Check counts: it may already have been saved.",
                      );
                    }}
                  >
                    Dismiss request
                  </button>
                </div>
              )}
              {expanded && (
                <section className="aidomme-tracker-menu">
                  <GuidedMenu metronome={metronome.controls} />
                  <p>
                    <b>{snapshot.edgesSinceLastOrgasm}</b> edges since the last
                    recorded orgasm (
                    {snapshot.lastOrgasmAtUtc
                      ? new Date(snapshot.lastOrgasmAtUtc).toLocaleString()
                      : "unknown"}
                    ).
                  </p>
                  <small>
                    {media
                      ? `Recording on ${media.kind === "video" ? "video" : "image"} #${media.id}`
                      : "No active media: events are recorded without a like."}
                  </small>
                  <button
                    onClick={() => setSettingsOpen(!settingsOpen)}
                    aria-expanded={settingsOpen}
                  >
                    Tracker settings
                  </button>
                  {settingsOpen && settings && (
                    <div className="aidomme-tracker-settings">
                      <label>
                        <input
                          type="checkbox"
                          checked={settings.autoLike}
                          disabled={busy}
                          onChange={(e) =>
                            void mutate("/settings", "PUT", {
                              ...settings,
                              autoLike: e.target.checked,
                            })
                          }
                        />{" "}
                        Add content likes
                      </label>
                      <label>
                        Likes per edge{" "}
                        <select
                          disabled={busy}
                          value={settings.edgeWeight}
                          onChange={(e) =>
                            void mutate("/settings", "PUT", {
                              ...settings,
                              edgeWeight: Number(e.target.value),
                            })
                          }
                        >
                          <option value={0}>0</option>
                          <option value={1}>1</option>
                        </select>
                      </label>
                      <label>
                        Likes per orgasm{" "}
                        <select
                          disabled={busy}
                          value={settings.orgasmWeight}
                          onChange={(e) =>
                            void mutate("/settings", "PUT", {
                              ...settings,
                              orgasmWeight: Number(e.target.value),
                            })
                          }
                        >
                          {[0, 1, 2, 3].map((n) => (
                            <option key={n} value={n}>
                              {n}
                            </option>
                          ))}
                        </select>
                      </label>
                      <button
                        disabled={busy || !!retry}
                        onClick={async () => {
                          if (await mutate("/setting")) {
                            await refresh();
                            setNotice(
                              "Started a new tracking session. History retained.",
                            );
                          }
                        }}
                      >
                        New tracking session
                      </button>
                      <p>Shortcuts are configured in Cove's keyboard settings.</p>
                    </div>
                  )}
                </section>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
  // Body portal avoids inheriting the app floating layer's stacking order beneath lightboxes.
  return closed ? null : createPortal(content, portal || document.body);
}
