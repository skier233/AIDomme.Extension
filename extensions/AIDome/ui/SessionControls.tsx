import React, { useEffect, useRef, useState } from "react";
import {
  guidedApi,
  guidedClient,
  remainingSeconds,
  useGuidedSession,
} from "./GuidedClient";

export const defaultPolicy = {
  guidance: "Detailed",
  pacing: "Engine",
  media: "User",
  permission: "EngineFinish",
  gamesEnabled: true,
  allowedMethodIds: [] as number[],
  canChangeDuringSession: false,
};

export function SessionPolicyEditor({
  value,
  onChange,
  disabled = false,
  active = false,
}: {
  value: typeof defaultPolicy;
  onChange: (value: typeof defaultPolicy) => void;
  disabled?: boolean;
  active?: boolean;
}) {
  const [methods, setMethods] = useState<any[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    let alive = true;
    guidedApi("/configuration")
      .then((data) => {
        if (alive) setMethods(data.methods.map((item: any) => item.value));
      })
      .catch((e) => {
        if (alive) setError(e.message);
      });
    return () => {
      alive = false;
    };
  }, []);
  const patch = (data: Partial<typeof defaultPolicy>) =>
    onChange({ ...value, ...data });
  const preset = (name: string) =>
    onChange({
      ...defaultPolicy,
      media: value.media,
      canChangeDuringSession: value.canChangeDuringSession,
      ...(name === "Simple"
        ? { guidance: "Simple" }
        : name === "Permission"
          ? {
              guidance: "Permission",
              pacing: "Free",
              permission: "RequestsOnly",
              gamesEnabled: false,
            }
          : {}),
    });
  return (
    <fieldset className="aidomme-session-policy" disabled={disabled}>
      <legend>Session choices</legend>
      <div className="aidomme-tracker-actions">
        <button type="button" onClick={() => preset("Detailed")}>
          Full guidance
        </button>
        <button type="button" onClick={() => preset("Simple")}>
          Simple guidance
        </button>
        <button type="button" onClick={() => preset("Permission")}>
          Permission focused
        </button>
      </div>
      <label>
        Guidance
        <select
          value={value.guidance}
          onChange={(e) =>
            e.target.value === "Permission"
              ? preset("Permission")
              : patch({ guidance: e.target.value })
          }
        >
          <option value="Detailed">Detailed methods</option>
          <option value="Simple">Simple methods</option>
          <option value="Permission">Permission focused</option>
        </select>
      </label>
      <label>
        Pacing
        <select
          value={value.pacing}
          disabled={value.guidance === "Permission"}
          onChange={(e) => patch({ pacing: e.target.value })}
        >
          <option value="Engine">Domme sets pace</option>
          <option value="Free">My own pace</option>
        </select>
      </label>
      <label>
        Media
        <select
          value={value.media}
          onChange={(e) => patch({ media: e.target.value })}
        >
          <option value="User">I browse Cove</option>
          <option value="Engine">Domme chooses content</option>
        </select>
      </label>
      <label>
        Permission
        <select
          value={value.permission}
          disabled={value.guidance === "Permission"}
          onChange={(e) => patch({ permission: e.target.value })}
        >
          <option value="EngineFinish">Domme can decide at session end</option>
          <option value="RequestsOnly">Decide when I request permission</option>
        </select>
      </label>
      <label>
        <input
          type="checkbox"
          checked={value.gamesEnabled}
          disabled={value.guidance === "Permission"}
          onChange={(e) => patch({ gamesEnabled: e.target.checked })}
        />{" "}
        Scheduled games/challenges
      </label>
      {!active && (
        <label>
          <input
            type="checkbox"
            checked={value.canChangeDuringSession}
            onChange={(e) =>
              patch({ canChangeDuringSession: e.target.checked })
            }
          />{" "}
          Allow settings changes during this session
        </label>
      )}
      {value.guidance !== "Permission" && (
        <details>
          <summary>
            Allowed methods ({value.allowedMethodIds.length || "all eligible"})
          </summary>
          <button type="button" onClick={() => patch({ allowedMethodIds: [] })}>
            Use all eligible methods
          </button>
          <div className="aidomme-method-choices">
            {methods.map((method) => (
              <label key={method.id}>
                <input
                  type="checkbox"
                  checked={value.allowedMethodIds.includes(method.id)}
                  onChange={(e) =>
                    patch({
                      allowedMethodIds: e.target.checked
                        ? [...value.allowedMethodIds, method.id]
                        : value.allowedMethodIds.filter(
                            (id) => id !== method.id,
                          ),
                    })
                  }
                />
                {method.name}
              </label>
            ))}
          </div>
          <small>
            Empty selection uses all eligible methods. Simple guidance
            additionally limits selection to basic hand methods; toy choices
            still apply.
          </small>
        </details>
      )}
      {value.guidance === "Permission" && (
        <small>
          Free browsing and permission requests; structured offers and
          permission challenges arrive in phase 5.
        </small>
      )}
      {error && <p role="alert">{error}</p>}
    </fieldset>
  );
}

export function GameControls({
  online,
  submit,
  disabled,
}: {
  online: any;
  submit: (answer: number) => void;
  disabled?: boolean;
}) {
  const [answer, setAnswer] = useState(0);
  if (!online?.activeGameChallengeId) return null;
  if (!online.activeGameAnswerReady)
    return (
      <div>
        <small>
          Follow the current challenge; its answer controls appear when ready.
        </small>
        {online.activeGameType === "EdgeEndurance" && (
          <button disabled={disabled} onClick={() => submit(0)}>
            Had to stop to avoid going over
          </button>
        )}
      </div>
    );
  const choices: Record<string, [string, number][]> = {
    CoinFlip: [
      ["Heads", 0],
      ["Tails", 1],
    ],
    DoubleOrNothing: [
      ["Heads", 0],
      ["Tails", 1],
    ],
    PickYourPoison: [
      ["Option 1", 1],
      ["Option 2", 2],
    ],
    RerollGamble: [
      ["Accept", 0],
      ...(online.activeGameRange === 0
        ? []
        : [["Reroll", 1] as [string, number]]),
    ],
    RedLightGreenLight: [
      ["Obeyed every command", 1],
      ["Slipped", 0],
    ],
    CardDraw: [
      ["High (7+)", 1],
      ["Low (1–6)", 0],
    ],
    NumberGuess: Array.from(
      { length: Math.min(30, Math.max(0, online.activeGameRange)) },
      (_, i) => [String(i + 1), i + 1],
    ),
  };
  return (
    <fieldset disabled={disabled}>
      <legend>{online.activeGameType}</legend>
      {choices[online.activeGameType] ? (
        <div className="aidomme-tracker-actions">
          {choices[online.activeGameType].map(([label, value]) => (
            <button key={value} onClick={() => submit(value)}>
              {label}
            </button>
          ))}
        </div>
      ) : (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            submit(answer);
          }}
        >
          <label>
            Answer
            <input
              aria-label="Challenge answer"
              type="number"
              value={answer}
              onChange={(e) => setAnswer(Number(e.target.value))}
            />
          </label>
          <button type="submit">Submit answer</button>
        </form>
      )}
    </fieldset>
  );
}

/** Instruction-style actions the user can send; which ones apply depends on the step and mode. */
function sessionActions(snapshot: any, online: any) {
  return (snapshot.actions || []).filter(
    (action: any) =>
      !["edged", "orgasm"].includes(action.id) &&
      (action.id !== "done" ||
        online?.isLubePause ||
        online?.currentDirective?.kind === "Finish") &&
      !(
        snapshot.policy.guidance === "Permission" &&
        ["done", "beg-to-stop"].includes(action.id)
      ),
  );
}

function formatRemaining(seconds: number) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

/**
 * The live session view. `compact` is the floating companion: the current instruction, a short
 * chat log, and only the controls the user holds. The full variant is the AI Domme page.
 * Outside debug sessions there is no End: the user can pause, and the domme decides the ending.
 */
export function GuidedPanel({ compact = false }: { compact?: boolean }) {
  const state = useGuidedSession();
  const snapshot = state.snapshot,
    online = snapshot?.online;
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(defaultPolicy);
  const [draftRevision, setDraftRevision] = useState(0);
  const logRef = useRef<HTMLOListElement | null>(null);
  const messages: any[] = snapshot?.messages || [];
  const log = compact ? messages.slice(-12) : messages;
  useEffect(() => {
    if (logRef.current) logRef.current.scrollTop = logRef.current.scrollHeight;
  }, [log.length, log.at(-1)?.id]);
  if (!snapshot?.sessionActive)
    return state.error ? <p role="alert">{state.error}</p> : null;
  const remaining = remainingSeconds(state);
  const text = online?.currentDirective?.text;
  const blocked = state.busy || !!state.pending || online?.isPaused;
  const canEnd = !!online?.isDebugSession;
  return (
    <section
      className={`aidomme-guided-panel${compact ? " is-compact" : ""}`}
      aria-label="Guided session"
    >
      <div className="aidomme-session-status">
        <strong>{online?.isPaused ? "Paused" : "In session"}</strong>
        <span>{online?.edgesThisSession || 0} edges</span>
        {remaining >= 0 && (
          <output aria-label="Step remaining">
            {formatRemaining(remaining)}
          </output>
        )}
      </div>
      {text && <p className="aidomme-current-command">{text}</p>}
      <ol className="aidomme-companion-chat" ref={logRef} aria-label="Chat log">
        {log
          // The companion already shows the current instruction above the log.
          .filter((message: any) => !(compact && message.content === text))
          .map((message: any) => (
          <li
            key={message.id}
            className={`from-${String(message.sender).toLowerCase()}`}
          >
            <strong>{message.sender === "Domme" ? "Domme" : message.sender}</strong>{" "}
            {message.content}
          </li>
        ))}
      </ol>
      <GameControls
        online={online}
        disabled={blocked}
        submit={(answer) => {
          void guidedClient.control("game-answer", answer);
        }}
      />
      {!compact && (
        <div className="aidomme-tracker-actions">
          <button
            disabled={state.busy || !!state.pending}
            onClick={() => void guidedClient.record("edge")}
          >
            Edge
          </button>
          <button
            disabled={state.busy || !!state.pending}
            onClick={() => void guidedClient.record("orgasm")}
          >
            Orgasm
          </button>
        </div>
      )}
      <div className="aidomme-tracker-actions">
        {sessionActions(snapshot, online).map((action: any) => (
          <button
            disabled={blocked}
            key={action.id}
            onClick={() => void guidedClient.control(action.id)}
          >
            {action.label}
          </button>
        ))}
        <button
          onClick={() =>
            void guidedClient.control(online?.isPaused ? "resume" : "pause")
          }
        >
          {online?.isPaused ? "Resume" : "Pause"}
        </button>
        {canEnd && (
          <button onClick={() => void guidedClient.control("end")}>
            End (debug)
          </button>
        )}
      </div>
      {!compact && (
        <>
          <details>
            <summary>Report an infraction</summary>
            <button
              disabled={blocked}
              onClick={() =>
                void guidedClient.mutate("/session/infraction", {
                  type: "Disobedience",
                })
              }
            >
              Disobedience
            </button>
            <button
              disabled={blocked}
              onClick={() =>
                void guidedClient.mutate("/session/infraction", {
                  type: "RuinedOrgasm",
                })
              }
            >
              Ruined orgasm
            </button>
          </details>
          <GuidedMenu />
          <button
            disabled={!snapshot.policy.canChangeDuringSession}
            onClick={() => {
              setDraft(snapshot.policy);
              setDraftRevision(snapshot.policyRevision);
              setEditing(!editing);
            }}
          >
            Session settings{" "}
            {snapshot.policy.canChangeDuringSession ? "" : "(locked)"}
          </button>
          {editing && (
            <>
              <SessionPolicyEditor
                value={draft}
                onChange={setDraft}
                active
                disabled={state.busy}
              />
              <button
                disabled={state.busy || !!state.pending}
                onClick={async () => {
                  const response = await guidedClient.mutate(
                    "/session/policy",
                    {
                      sessionId: online.sessionId,
                      revision: draftRevision,
                      policy: draft,
                    },
                    "PUT",
                  );
                  if (response) setEditing(false);
                }}
              >
                Save session settings
              </button>
            </>
          )}
        </>
      )}
      {state.error && <p role="alert">{state.error}</p>}
      {state.pending && (
        <div className="aidomme-tracker-actions">
          <button
            disabled={state.busy}
            onClick={() => void guidedClient.retry()}
          >
            Retry same request
          </button>
          <button disabled={state.busy} onClick={() => guidedClient.dismiss()}>
            Dismiss request
          </button>
        </div>
      )}
    </section>
  );
}

/**
 * Secondary, per-browser options: narration, directed media, and the metronome. When the domme
 * owns pacing the metronome is hers, so its controls are not offered here.
 */
export function GuidedMenu({ metronome }: { metronome?: React.ReactNode }) {
  const state = useGuidedSession();
  const snapshot = state.snapshot,
    online = snapshot?.online;
  const active = !!snapshot?.sessionActive;
  const dommePaces = active && snapshot.policy?.pacing === "Engine";
  return (
    <div className="aidomme-guided-menu">
      {active && (
        <label>
          <input
            type="checkbox"
            disabled={online?.isPaused}
            checked={state.voiceEnabled}
            onChange={(e) => void guidedClient.setVoice(e.target.checked)}
          />{" "}
          Narrate in this browser
        </label>
      )}
      {state.voiceStatus && <small role="status">{state.voiceStatus}</small>}
      {active && snapshot.policy.media === "Engine" && (
        <>
          <label>
            <input
              type="checkbox"
              disabled={online?.isPaused}
              checked={state.directing}
              onChange={(e) =>
                void guidedClient.setDirectedMedia(e.target.checked)
              }
            />{" "}
            Show directed media in this browser
          </label>
          {state.mediaStatus && (
            <small role="status">{state.mediaStatus}</small>
          )}
        </>
      )}
      {metronome &&
        (dommePaces ? (
          <small>The domme sets the pace in this session.</small>
        ) : (
          <details>
            <summary>Metronome</summary>
            {metronome}
          </details>
        ))}
    </div>
  );
}
