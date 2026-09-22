import React, { useEffect, useState } from "react";
import { extensionFetch } from "@cove/runtime/api";

type Definition = { id: number; name: string; [key: string]: any };
type View = {
  value: Definition;
  isOverride: boolean;
  sharedDefault?: Definition;
};
type Configuration = {
  methods: View[];
  enders: View[];
  ratings: View[];
  comparisons: View[];
};
const methodFields = [
  ["name", "Name", "text"],
  ["description", "Instructions", "textarea"],
  ["intensity", "Intensity", "scale"],
  ["tease", "Tease", "scale"],
  ["tortLevel", "Torture level", "scale"],
  ["closeness", "Closeness", "scale"],
  ["requiresLube", "Requires lube", "checkbox"],
  ["toyCategory", "Toy", "toy"],
  ["timePointMultiplier", "Points per second", "number"],
  ["edgePointMultiplier", "Points per edge", "number"],
];
const enderFields = [
  ["name", "Name", "text"],
  ["announcementText", "Announcement", "textarea"],
  ["instructionText", "Instructions", "textarea"],
  ["severity", "Severity", "scale"],
  ["anticipation", "Anticipation", "scale"],
  ["humiliation", "Humiliation", "scale"],
  ["requiresBondage", "Requires bondage", "checkbox"],
  ["requiresCEI", "Requires CEI", "checkbox"],
  ["requiresSelfOral", "Requires self-oral", "checkbox"],
];

async function request(path = "", method = "GET", body?: unknown) {
  const response = await extensionFetch(
    `/api/ext/aidome/configuration${path}`,
    {
      method,
      headers: { "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    },
  );
  const text = await response.text();
  let data;
  try {
    data = JSON.parse(text);
  } catch {
    throw new Error(
      "Configuration is unavailable. Check that the updated AI Domme backend is loaded.",
    );
  }
  if (!response.ok)
    throw new Error(
      data.message || data.detail || "Could not save configuration.",
    );
  return data;
}

export function DefinitionSettings() {
  const [data, setData] = useState<Configuration | null>(null);
  const [kind, setKind] = useState<"methods" | "enders">("methods");
  const [selected, setSelected] = useState(1);
  const [draft, setDraft] = useState<Definition | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  useEffect(() => {
    let active = true;
    request()
      .then((value) => {
        if (active) setData(value);
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, []);
  const view = data?.[kind].find((x) => x.value.id === selected);
  useEffect(() => {
    setDraft(view ? { ...view.value } : null);
  }, [view, kind, selected]);
  const mutate = async (operation: () => Promise<any>, message: string) => {
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await operation();
      setData(await request());
      setNotice(message);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  if (!data) return <p>{error || "Loading definitions…"}</p>;
  return (
    <div className="setup-card aidomme-definitions">
      <p>
        Shared defaults are available to every Cove account. Saving creates your
        personal override. Changes apply to newly selected methods; start a new
        guided session to use an updated method set.
      </p>
      <fieldset disabled={busy}>
        <label>
          Definition type
          <select
            value={kind}
            onChange={(e) => {
              const next = e.target.value as typeof kind;
              setKind(next);
              setSelected(data[next][0]?.value.id || 1);
              setNotice("");
            }}
          >
            <option value="methods">Stroking methods</option>
            <option value="enders">Punishment endings</option>
          </select>
        </label>
        <label>
          Definition
          <select
            value={selected}
            onChange={(e) => {
              setSelected(Number(e.target.value));
              setNotice("");
            }}
          >
            {data[kind].map((x) => (
              <option key={x.value.id} value={x.value.id}>
                {x.value.name}
                {x.isOverride ? " · personal" : ""}
              </option>
            ))}
          </select>
        </label>
        <p>
          {view?.isOverride ? "Your personal override" : "Using shared default"}
        </p>
        {draft && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void mutate(
                () => request(`/${kind}/${draft.id}`, "PUT", draft),
                "Personal override saved.",
              );
            }}
          >
            {(kind === "methods" ? methodFields : enderFields).map(
              ([key, label, type]) => (
                <label key={key}>
                  {label}
                  {type === "textarea" ? (
                    <textarea
                      value={draft[key] || ""}
                      onChange={(e) =>
                        setDraft({ ...draft, [key]: e.target.value })
                      }
                    />
                  ) : type === "toy" ? (
                    <select
                      value={draft[key]}
                      onChange={(e) =>
                        setDraft({ ...draft, [key]: e.target.value })
                      }
                    >
                      {[
                        "None",
                        "HeadVibrator",
                        "Fleshlight",
                        "Autoblow",
                        "ProstateMassager",
                      ].map((toy) => (
                        <option key={toy} value={toy}>
                          {toy}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <input
                      type={type === "scale" ? "number" : type}
                      min={
                        type === "number" || type === "scale" ? 0 : undefined
                      }
                      max={type === "scale" ? 10 : undefined}
                      step={type === "number" ? "any" : undefined}
                      checked={type === "checkbox" ? !!draft[key] : undefined}
                      value={
                        type === "checkbox" ? undefined : (draft[key] ?? "")
                      }
                      onChange={(e) =>
                        setDraft({
                          ...draft,
                          [key]:
                            type === "checkbox"
                              ? e.target.checked
                              : type === "number" || type === "scale"
                                ? Number(e.target.value)
                                : e.target.value,
                        })
                      }
                    />
                  )}
                </label>
              ),
            )}
            <button type="submit">Save personal override</button>
            <button
              type="button"
              disabled={!view?.isOverride || !view.sharedDefault}
              onClick={() =>
                void mutate(
                  () => request(`/${kind}/${draft.id}/override`, "DELETE"),
                  "Shared default restored.",
                )
              }
            >
              Restore shared default
            </button>
          </form>
        )}
        <p>
          Calibration preserved: {data.ratings.length} ratings and{" "}
          {data.comparisons.length} comparisons.
        </p>
        <button
          type="button"
          disabled={
            data.comparisons.filter((x) => x.value.mode === "Stim").length < 25
          }
          onClick={() =>
            void mutate(
              () => request("/optimize", "POST"),
              "Method multipliers optimized for your account.",
            )
          }
        >
          Optimize my method multipliers
        </button>
      </fieldset>
      {notice && <p role="status">{notice}</p>}
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
