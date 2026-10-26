import React, { useEffect, useRef, useState } from "react";
import { useExtensionKeyboardBindings } from "@cove/runtime/components";
import {
  guidedClient,
  remainingSeconds,
  useGuidedSession,
} from "./GuidedClient";
import { FollowUps, Obligations, OfferCard } from "./Ledger";

export const EXTENSION_ID = "cove.community.aidome";

export function formatRemaining(seconds: number) {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

/** "Alt+B" style hint for a registered action, or "" when unbound. */
export function useHotkeyHints() {
  const bindings = useExtensionKeyboardBindings(EXTENSION_ID) || {};
  return (id: string) => (bindings[id] || []).join(" / ");
}

/** Answer controls for the running game: whatever the server says the game accepts. */
export function GameControls({
  game,
  submit,
  disabled,
}: {
  game: any;
  submit: (answer: number) => void;
  disabled?: boolean;
}) {
  const [answer, setAnswer] = useState("");
  if (!game) return null;
  if (game.input === "Choices")
    return (
      <fieldset className="aidomme-game" disabled={disabled}>
        <legend>{game.name}</legend>
        <div className="aidomme-actions">
          {game.choices.map((choice: any) => (
            <button key={choice.value} onClick={() => submit(choice.value)}>
              {choice.label}
            </button>
          ))}
        </div>
      </fieldset>
    );
  if (game.input === "Number")
    return (
      <fieldset className="aidomme-game" disabled={disabled}>
        <legend>{game.name}</legend>
        <form
          className="aidomme-actions"
          onSubmit={(e) => {
            e.preventDefault();
            if (answer.trim() !== "") submit(Number(answer));
            setAnswer("");
          }}
        >
          <input
            aria-label="Your answer"
            type="number"
            value={answer}
            onChange={(e) => setAnswer(e.target.value)}
          />
          <button type="submit">Answer</button>
        </form>
      </fieldset>
    );
  return (
    <p className="aidomme-game-note">
      <strong>{game.name}</strong> Follow her instruction; your edges count
      automatically.
    </p>
  );
}

/** The session buttons, each with its hotkey. Only controls the user holds are shown. */
export function SessionActions({
  session,
  withTracking,
}: {
  session: any;
  withTracking?: boolean;
}) {
  const state = useGuidedSession();
  const hint = useHotkeyHints();
  const blocked = state.busy || !!state.pending || session.isPaused;
  const label = (text: string, id: string) =>
    hint(id) ? (
      <>
        {text} <kbd>{hint(id)}</kbd>
      </>
    ) : (
      text
    );
  return (
    <div className="aidomme-actions">
      {withTracking && (
        <>
          <button
            className="is-primary"
            disabled={state.busy || !!state.pending}
            onClick={() => void guidedClient.record("edge")}
          >
            {label("Edge", "tracker-edge")}
          </button>
          <button
            disabled={state.busy || !!state.pending}
            onClick={() => void guidedClient.record("orgasm")}
          >
            {label("Orgasm", "tracker-orgasm")}
          </button>
        </>
      )}
      {session.canRequestPermission && (
        <button
          disabled={blocked}
          onClick={() => void guidedClient.control("request-permission")}
        >
          {label("Ask to cum", "session-permission")}
        </button>
      )}
      {session.canRequestRest && (
        <button
          disabled={blocked}
          onClick={() => void guidedClient.control("request-rest")}
        >
          {label("Beg for a rest", "session-stop-request")}
        </button>
      )}
      {session.canComplete && (
        <button
          disabled={blocked}
          onClick={() => void guidedClient.control("done")}
        >
          {label(
            session.currentDirective?.kind === "Finish" ? "Accept" : "Ready",
            "session-complete",
          )}
        </button>
      )}
      <button
        disabled={session.interrupted}
        onClick={() =>
          void guidedClient.control(session.isPaused ? "resume" : "pause")
        }
      >
        {label(session.isPaused ? "Resume" : "Pause", "session-pause")}
      </button>
      {session.isDebugSession && (
        <button onClick={() => void guidedClient.control("end")}>
          End (debug)
        </button>
      )}
    </div>
  );
}

/**
 * Her words since the current order, shown right under it so nothing she says gets lost in the
 * log: what she said with the order, and every reaction and aside after it. The newest stands out.
 */
export function HerLatest({ messages, order }: { messages: any[]; order?: string }) {
  let at = -1;
  for (let i = messages.length - 1; i >= 0; i--)
    if (messages[i].sender === "Domme" && (messages[i].kind === "order" || messages[i].content === order)) {
      at = i;
      break;
    }
  if (at < 0) return null;
  const moment = Date.parse(messages[at].timestamp);
  // Lines spoken in the same moment as the order (a segment's opening words) belong with it.
  let from = at;
  while (
    from > 0 &&
    messages[from - 1].sender === "Domme" &&
    messages[from - 1].kind !== "order" &&
    Math.abs(Date.parse(messages[from - 1].timestamp) - moment) < 1500
  )
    from--;
  const lines = messages
    .slice(from)
    .filter((m, i) => m.sender === "Domme" && from + i !== at && m.content !== order)
    .slice(-4);
  if (lines.length === 0) return null;
  return (
    <ul className="aidomme-latest" aria-label="What she said" aria-live="polite">
      {lines.map((m, i) => (
        <li
          key={m.id}
          className={`kind-${m.kind || "say"}${i === lines.length - 1 ? " is-newest" : ""}`}
        >
          {m.content}
        </li>
      ))}
    </ul>
  );
}

/** The chat log: the domme's words, with notices de-emphasized. */
export function ChatLog({
  messages,
  hide,
  className,
}: {
  messages: any[];
  hide?: string;
  className?: string;
}) {
  const ref = useRef<HTMLOListElement | null>(null);
  const visible = messages.filter((message) => message.content !== hide);
  useEffect(() => {
    if (ref.current) ref.current.scrollTop = ref.current.scrollHeight;
  }, [visible.length, visible.at(-1)?.id]);
  return (
    <ol
      className={`aidomme-chat ${className || ""}`}
      ref={ref}
      aria-label="Chat log"
    >
      {visible.map((message) => (
        <li
          key={message.id}
          className={`from-${String(message.sender).toLowerCase()} kind-${message.kind || "say"}`}
        >
          {message.sender !== "Domme" && <strong>{message.sender} </strong>}
          {message.content}
        </li>
      ))}
    </ol>
  );
}

/** Time left in the instruction and edges still to reach. */
export function SessionStatus({ session }: { session: any }) {
  const state = useGuidedSession();
  const remaining = remainingSeconds(state);
  return (
    <div className="aidomme-status" role="status">
      <span>
        {session.interrupted
          ? "Stopped: tell her what happened"
          : session.isPaused
            ? "Paused"
            : "In session"}
      </span>
      {session.edgesRemaining != null && (
        <span>
          <b>{session.edgesRemaining}</b>{" "}
          {session.edgesRemaining === 1 ? "edge" : "edges"} to go
        </span>
      )}
      <span>{session.edgesThisSession} edges so far</span>
      {remaining >= 0 && (
        <output aria-label="Time left">{formatRemaining(remaining)}</output>
      )}
    </div>
  );
}

/**
 * The floating companion's session view: the current instruction, a short chat log, and only
 * the controls the user holds in this mode. Edge and Orgasm come from the tracker around it.
 */
export function GuidedPanel() {
  const state = useGuidedSession();
  const session = state.snapshot?.session;
  if (!state.snapshot?.sessionActive || !session)
    return state.error ? (
      <p role="alert">{state.error}</p>
    ) : (
      <FollowUps compact />
    );
  const text = session.currentDirective?.text;
  const blocked = state.busy || !!state.pending || session.isPaused;
  return (
    <section className="aidomme-guided-panel" aria-label="Guided session">
      <FollowUps compact />
      <SessionStatus session={session} />
      <Obligations session={session} />
      {text && <p className="aidomme-current-command">{text}</p>}
      <HerLatest messages={state.snapshot.messages || []} order={text} />
      <OfferCard session={session} />
      <ChatLog
        messages={(state.snapshot.messages || []).slice(-12)}
        hide={text}
        className="is-compact"
      />
      <GameControls
        game={session.game}
        disabled={blocked}
        submit={(answer) => void guidedClient.control("game-answer", answer)}
      />
      <SessionActions session={session} />
      {state.error && <p role="alert">{state.error}</p>}
      <PendingRequest />
    </section>
  );
}

export function PendingRequest() {
  const state = useGuidedSession();
  if (!state.pending) return null;
  return (
    <div className="aidomme-actions">
      <button disabled={state.busy} onClick={() => void guidedClient.retry()}>
        Retry same request
      </button>
      <button disabled={state.busy} onClick={() => guidedClient.dismiss()}>
        Dismiss request
      </button>
    </div>
  );
}

/**
 * Per-browser options: narration and the metronome. When the domme sets the pace, the metronome
 * follows her and its tempo is not the user's to change.
 */
export function GuidedMenu({ metronome }: { metronome?: React.ReactNode }) {
  const state = useGuidedSession();
  const session = state.snapshot?.session;
  return (
    <div className="aidomme-guided-menu">
      {session && (
        <label>
          <input
            type="checkbox"
            disabled={session.isPaused}
            checked={state.voiceEnabled}
            onChange={(e) => void guidedClient.setVoice(e.target.checked)}
          />{" "}
          Narrate in this browser
        </label>
      )}
      {state.voiceStatus && <small role="status">{state.voiceStatus}</small>}
      {metronome && (
        <details>
          <summary>
            {session?.contract?.pacing === "Domme"
              ? "Metronome (her pace)"
              : "Metronome"}
          </summary>
          {metronome}
        </details>
      )}
    </div>
  );
}
