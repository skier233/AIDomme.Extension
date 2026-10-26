import React, { useState } from "react";
import { guidedClient, useGuidedSession } from "./GuidedClient";
import { useHotkeyHints } from "./SessionControls";

const FAR_FUTURE = 9000; // DateTime.MaxValue: "for the whole session".

function when(utc: string) {
  return new Date(utc).toLocaleString(undefined, {
    weekday: "short",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  });
}

function until(utc?: string | null) {
  if (!utc) return null;
  return new Date(utc).getUTCFullYear() >= FAR_FUTURE
    ? "this session"
    : `until ${when(utc)}`;
}

/** Her questions: what was observed, and the answers she accepts. Nothing else happens until they are answered. */
export function FollowUps({ compact = false }: { compact?: boolean }) {
  const state = useGuidedSession();
  const questions: any[] = state.snapshot?.followUps || [];
  if (!questions.length) return null;
  return (
    <section
      className={`aidomme-questions${compact ? " is-compact" : ""}`}
      aria-label="Her questions"
    >
      {questions.map((q) => (
        <article key={q.id} className="aidomme-question">
          <strong>{q.question}</strong>
          <p className="aidomme-muted">
            {q.observed} {!compact && `(${when(q.occurredAtUtc)})`}
          </p>
          <div className="aidomme-actions">
            {q.answers.map((a: any) => (
              <button
                key={a.value}
                disabled={state.busy}
                onClick={() =>
                  void guidedClient.mutate(`/followups/${q.id}/answer`, {
                    answer: a.value,
                  })
                }
              >
                {a.label}
              </button>
            ))}
          </div>
        </article>
      ))}
    </section>
  );
}

/** Her deal on the table: accept or decline, both on hotkeys. */
export function OfferCard({ session }: { session: any }) {
  const state = useGuidedSession();
  const hint = useHotkeyHints();
  const offer = session.offer;
  if (!offer) return null;
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
    <div className="aidomme-offer" role="group" aria-label="Her offer">
      <p>
        <strong>Her offer</strong>
        {offer.secondsLeft > 0 && (
          <span className="aidomme-muted"> · {offer.secondsLeft}s to decide</span>
        )}
      </p>
      <p>{offer.text}</p>
      <div className="aidomme-actions">
        <button
          className="is-primary"
          disabled={blocked}
          onClick={() => void guidedClient.control("offer-accept")}
        >
          {label("Accept", "offer-accept")}
        </button>
        <button
          disabled={blocked}
          onClick={() => void guidedClient.control("offer-decline")}
        >
          {label("Decline", "offer-decline")}
        </button>
      </div>
    </div>
  );
}

/** What this session is holding the user to. */
export function Obligations({ session }: { session: any }) {
  const parts: string[] = [];
  const denial = until(session.noOrgasmUntilUtc);
  if (denial) parts.push(`No orgasm ${denial}`);
  if (session.owedEdges > 0)
    parts.push(`${session.owedEdges} owed ${session.owedEdges === 1 ? "edge" : "edges"}`);
  if (session.owedRuins > 0)
    parts.push(
      session.owedRuins === 1
        ? "Next orgasm ruined"
        : `Next ${session.owedRuins} orgasms ruined`,
    );
  if (session.owesPunishmentEnding) parts.push("Ends with a punishment ending");
  if (session.contract?.punishment) parts.push("Punishment session");
  if (!parts.length) return null;
  return (
    <p className="aidomme-owed" aria-label="What you owe">
      {parts.join(" · ")}
    </p>
  );
}

const TRAITS: [string, string][] = [
  ["long", "Long"],
  ["intense", "Intense"],
  ["torturous", "Torturous"],
  ["mean", "Mean"],
  ["teasing", "Teasing"],
];

/** Promise her something: picked, not typed. She answers in the chat. */
export function PromiseForm() {
  const state = useGuidedSession();
  const [scope, setScope] = useState<"Session" | "Days">("Session");
  const [days, setDays] = useState(3);
  const [noOrgasm, setNoOrgasm] = useState(true);
  const [traits, setTraits] = useState<Record<string, boolean>>({});
  const [note, setNote] = useState("");
  const any = noOrgasm || Object.values(traits).some(Boolean);
  const propose = async () => {
    const response = await guidedClient.mutate("/commitments/propose", {
      terms: { scope, days: scope === "Days" ? days : 0, noOrgasm, ...traits, note: note || null },
    });
    if (response) setNote("");
  };
  return (
    <div className="aidomme-promise">
      <h3>Promise her something</h3>
      <div className="aidomme-chips">
        <label className="aidomme-check">
          <input
            type="checkbox"
            checked={noOrgasm}
            onChange={(e) => setNoOrgasm(e.target.checked)}
          />
          No orgasm
        </label>
        {TRAITS.map(([key, label]) => (
          <label className="aidomme-check" key={key}>
            <input
              type="checkbox"
              checked={!!traits[key]}
              onChange={(e) => setTraits({ ...traits, [key]: e.target.checked })}
            />
            {label}
          </label>
        ))}
      </div>
      <div className="aidomme-chips" role="radiogroup" aria-label="For how long">
        <label className="aidomme-check">
          <input
            type="radio"
            name="aidomme-promise-scope"
            checked={scope === "Session"}
            onChange={() => setScope("Session")}
          />
          Next session
        </label>
        <label className="aidomme-check">
          <input
            type="radio"
            name="aidomme-promise-scope"
            checked={scope === "Days"}
            onChange={() => setScope("Days")}
          />
          For
          <input
            type="number"
            min={1}
            max={60}
            value={days}
            aria-label="Days"
            disabled={scope !== "Days"}
            onChange={(e) => setDays(Math.max(1, Math.min(60, Number(e.target.value) || 1)))}
          />
          days
        </label>
      </div>
      <input
        className="aidomme-note"
        type="text"
        maxLength={500}
        placeholder="Note (optional)"
        value={note}
        onChange={(e) => setNote(e.target.value)}
      />
      <div className="aidomme-actions">
        <button disabled={!any || state.busy} onClick={() => void propose()}>
          Propose
        </button>
      </div>
    </div>
  );
}

/** Promises (pending, in force, recently settled) and what is owed. */
export function LedgerPanel({ ledger }: { ledger: any }) {
  const state = useGuidedSession();
  if (!ledger) return null;
  const pending = ledger.commitments.filter((c: any) => c.status === "Pending");
  const active = ledger.commitments.filter((c: any) => c.status === "Active");
  const settled = ledger.commitments.filter(
    (c: any) => !["Pending", "Active"].includes(c.status),
  );
  const owed = ledger.entries.filter((e: any) => e.status !== "Served");
  const served = ledger.entries.filter((e: any) => e.status === "Served");
  return (
    <>
      {pending.map((c: any) => (
        <div key={c.id} className="aidomme-offer" role="group" aria-label="Her answer">
          <p>{c.response}</p>
          <div className="aidomme-actions">
            <button
              className="is-primary"
              disabled={state.busy}
              onClick={() => void guidedClient.mutate(`/commitments/${c.id}/accept`, {})}
            >
              Accept her terms
            </button>
            <button
              disabled={state.busy}
              onClick={() => void guidedClient.mutate(`/commitments/${c.id}/decline`, {})}
            >
              Take it back
            </button>
          </div>
        </div>
      ))}
      {active.length > 0 && (
        <>
          <h3>Your promises</h3>
          <ul className="aidomme-list">
            {active.map((c: any) => (
              <li key={c.id}>
                {c.description}
                {c.endsAtUtc && ` · until ${when(c.endsAtUtc)}`}
                <span className="aidomme-muted">
                  {" "}
                  · break it: {c.stakes.map((s: any) => s.description).join(", ")}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
      <h3>You owe her</h3>
      {owed.length ? (
        <ul className="aidomme-list">
          {owed.map((e: any) => (
            <li key={e.id}>
              {e.kind === "Denial" ? `Denial ${until(e.endsAtUtc)}` : e.description}
              <span className="aidomme-muted"> · {e.reason}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="aidomme-muted">Nothing. For now.</p>
      )}
      {(served.length > 0 || settled.length > 0) && (
        <details>
          <summary>Recently settled</summary>
          <ul className="aidomme-list">
            {settled.map((c: any) => (
              <li key={c.id}>
                {c.status === "Kept" ? "Kept" : c.status === "Broken" ? "Broken" : "Withdrawn"}: {c.description}
                {c.resolution && <span className="aidomme-muted"> · {c.resolution}</span>}
              </li>
            ))}
            {served.map((e: any) => (
              <li key={e.id}>
                Served: {e.kind === "Denial" ? `denial ${until(e.endsAtUtc)}` : e.description}
                <span className="aidomme-muted"> · {e.reason}</span>
              </li>
            ))}
          </ul>
        </details>
      )}
    </>
  );
}
