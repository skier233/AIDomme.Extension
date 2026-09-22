import React, { useCallback, useEffect, useState } from "react";
import { extensionFetch } from "@cove/runtime/api";

const base = "/api/ext/aidome/commitments";
const emptyDefinition = {
  id: "00000000-0000-0000-0000-000000000000",
  name: "",
  terms: "",
  unit: "items",
  target: 1,
  dueAfterDays: null as number | null,
  enabled: true,
};

export function CommitmentPanel({ compact = false }: { compact?: boolean }) {
  const [state, setState] = useState<any>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState(!compact);
  const [definition, setDefinition] = useState({ ...emptyDefinition });
  const load = useCallback(async () => {
    try {
      const response = await extensionFetch(base);
      if (!response.ok) throw new Error(await response.text());
      setState(await response.json());
      setError("");
    } catch (e: any) {
      // The companion may briefly run against an older backend during an upgrade.
      // Keep that isolated from tracker errors and retry quietly.
      if (!compact) setError(e.message || "Could not load commitments.");
    }
  }, []);
  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), 10000);
    return () => clearInterval(timer);
  }, [load]);
  const mutate = async (action: string, targetId?: string, extra: any = {}) => {
    if (!state || busy) return;
    setBusy(true);
    setError("");
    try {
      const response = await extensionFetch(`${base}/mutate`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          eventId: crypto.randomUUID(),
          expectedRevision: state.revision,
          action,
          targetId,
          ...extra,
        }),
      });
      const text = await response.text();
      if (!response.ok)
        throw new Error(
          JSON.parse(text || "{}").message || text || "Request failed.",
        );
      setState(JSON.parse(text));
    } catch (e: any) {
      setError(e.message || "Request failed.");
      void load();
    } finally {
      setBusy(false);
    }
  };
  if (!state && !error) return null;
  const pending =
    state?.offers?.filter((x: any) => x.status === "Proposed") || [];
  const active =
    state?.commitments?.filter((x: any) => x.status === "Active") || [];
  const unresolved = pending.length + active.length;
  return (
    <section
      className="aidomme-commitments"
      aria-label="Offers and commitments"
    >
      <button
        className="btn btn-ghost"
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        Offers & commitments{unresolved ? ` (${unresolved})` : ""}
      </button>
      {open && (
        <div>
          {pending.map((offer: any) => (
            <article key={offer.id} className="commitment-item">
              <strong>{offer.name}</strong>
              <p>{offer.terms}</p>
              <small>
                Target: {offer.target} {offer.unit}
                {offer.dueAfterDays != null
                  ? ` · due ${offer.dueAfterDays} day${offer.dueAfterDays === 1 ? "" : "s"} after acceptance`
                  : ""}
              </small>
              <div>
                <button
                  disabled={busy}
                  onClick={() => void mutate("accept", offer.id)}
                >
                  Explicitly accept
                </button>{" "}
                <button
                  disabled={busy}
                  onClick={() => void mutate("decline", offer.id)}
                >
                  Decline
                </button>
              </div>
            </article>
          ))}
          {active.map((item: any) => (
            <article key={item.id} className="commitment-item">
              <strong>{item.name}</strong>
              <p>{item.terms}</p>
              <progress
                value={Math.min(item.progress, item.target)}
                max={item.target}
              />{" "}
              <small>
                {item.progress} / {item.target} {item.unit}
              </small>
              <div>
                <button
                  disabled={busy}
                  onClick={() =>
                    void mutate("progress", item.id, { amount: 1 })
                  }
                >
                  +1 progress
                </button>
                <button
                  disabled={busy}
                  onClick={() =>
                    void mutate("report", item.id, {
                      outcome: "Completed",
                      degree: 100,
                    })
                  }
                >
                  Report completed
                </button>
                <button
                  disabled={busy}
                  onClick={() => {
                    const raw = prompt("Completion degree (0–100)", "50");
                    if (raw !== null) {
                      const degree = Number(raw);
                      if (
                        Number.isFinite(degree) &&
                        degree >= 0 &&
                        degree <= 100
                      )
                        void mutate("report", item.id, {
                          outcome: "Partial",
                          degree,
                        });
                      else
                        setError(
                          "Completion degree must be between 0 and 100.",
                        );
                    }
                  }}
                >
                  Report partial
                </button>
                <button
                  disabled={busy}
                  onClick={() =>
                    void mutate("report", item.id, {
                      outcome: "Broken",
                      degree: 0,
                    })
                  }
                >
                  Report broken
                </button>
              </div>
            </article>
          ))}
          {!compact && (
            <details>
              <summary>Define offers</summary>
              <label>
                Name{" "}
                <input
                  value={definition.name}
                  maxLength={100}
                  onChange={(e) =>
                    setDefinition({ ...definition, name: e.target.value })
                  }
                />
              </label>
              <label>
                Terms{" "}
                <textarea
                  value={definition.terms}
                  maxLength={2000}
                  onChange={(e) =>
                    setDefinition({ ...definition, terms: e.target.value })
                  }
                />
              </label>
              <label>
                Target{" "}
                <input
                  type="number"
                  min="1"
                  value={definition.target}
                  onChange={(e) =>
                    setDefinition({
                      ...definition,
                      target: Number(e.target.value),
                    })
                  }
                />
              </label>
              <label>
                Unit{" "}
                <input
                  value={definition.unit}
                  maxLength={40}
                  onChange={(e) =>
                    setDefinition({ ...definition, unit: e.target.value })
                  }
                />
              </label>
              <label>
                Due after days{" "}
                <input
                  type="number"
                  min="0"
                  placeholder="No due date"
                  value={definition.dueAfterDays ?? ""}
                  onChange={(e) =>
                    setDefinition({
                      ...definition,
                      dueAfterDays:
                        e.target.value === "" ? null : Number(e.target.value),
                    })
                  }
                />
              </label>
              <button
                disabled={
                  busy || !definition.name.trim() || !definition.terms.trim()
                }
                onClick={async () => {
                  await mutate("save-definition", undefined, { definition });
                  setDefinition({ ...emptyDefinition });
                }}
              >
                Save definition
              </button>
              {(state.definitions || []).map((item: any) => (
                <div key={item.id}>
                  <strong>{item.name}</strong> · {item.target} {item.unit}{" "}
                  <button
                    disabled={busy || !item.enabled}
                    onClick={() => void mutate("propose", item.id)}
                  >
                    Create offer
                  </button>
                  <button
                    disabled={busy}
                    onClick={() => {
                      setDefinition(item);
                    }}
                  >
                    Edit
                  </button>
                </div>
              ))}
            </details>
          )}
          {!unresolved && (
            <small>No pending offers or active commitments.</small>
          )}
        </div>
      )}
      {error && <p role="alert">{error}</p>}
    </section>
  );
}
