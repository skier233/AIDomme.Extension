import React from "react";

/**
 * Small layout pieces shared by AI Domme's settings panels and pages, in the style Cove's own
 * settings and the sibling extensions use: theme tokens (bg-card, border-border, text-secondary),
 * row cards with the label on the left and the control on the right. Everything must render
 * inside an element carrying one of the scope classes (aidomme-settings-panel, aidomme-shell) or
 * the bundled utilities do not apply.
 */

export const inputCls =
  "box-border w-full rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground outline-none transition focus:border-accent";
export const selectCls =
  "box-border rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground outline-none transition focus:border-accent";
export const primaryBtn =
  "inline-flex items-center gap-1.5 rounded-lg bg-accent px-4 py-2 text-sm font-semibold text-white transition hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-60";
export const secondaryBtn =
  "inline-flex items-center gap-1.5 rounded-lg border border-border bg-card px-3 py-2 text-sm font-medium text-secondary transition hover:border-accent/50 hover:bg-card-hover hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50";

/** A titled group inside a panel or page. */
export function Section({
  title,
  hint,
  aside,
  children,
}: {
  title: string;
  hint?: React.ReactNode;
  aside?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-xs font-semibold uppercase tracking-wide text-muted">{title}</div>
          {hint && <p className="mt-1 text-sm text-secondary">{hint}</p>}
        </div>
        {aside}
      </div>
      {children}
    </section>
  );
}

/** One setting: its name and a hint on the left, the control on the right (stacked on narrow screens). */
export function Row({
  label,
  hint,
  htmlFor,
  children,
}: {
  label: React.ReactNode;
  hint?: React.ReactNode;
  htmlFor?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 rounded-xl border border-border bg-card px-4 py-3 sm:flex-row sm:items-center sm:justify-between sm:gap-6">
      <div className="min-w-0">
        <label htmlFor={htmlFor} className="text-sm font-medium text-foreground">
          {label}
        </label>
        {hint && <p className="mt-0.5 text-xs text-secondary">{hint}</p>}
      </div>
      <div className="shrink-0 sm:w-80">{children}</div>
    </div>
  );
}

/** A range with its value beside it. */
export function Slider({
  id,
  label,
  value,
  min,
  max,
  step = 1,
  onChange,
  format,
}: {
  id?: string;
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  onChange: (value: number) => void;
  format?: (value: number) => string;
}) {
  return (
    <div className="flex items-center gap-3">
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        aria-label={label}
        className="h-2 w-full cursor-pointer accent-[var(--color-accent)]"
        onChange={(e) => onChange(Number(e.target.value))}
      />
      <output className="w-24 shrink-0 text-right text-sm font-medium tabular-nums text-foreground">
        {format ? format(value) : value}
      </output>
    </div>
  );
}

export function Select<T extends string>({
  id,
  label,
  value,
  options,
  onChange,
  disabled,
  className,
}: {
  id?: string;
  label: string;
  value: T;
  options: readonly (readonly [T, string])[];
  onChange: (value: T) => void;
  disabled?: boolean;
  className?: string;
}) {
  return (
    <select
      id={id}
      aria-label={label}
      value={value}
      disabled={disabled}
      className={`${selectCls} ${className ?? "w-full"} disabled:opacity-50`}
      onChange={(e) => onChange(e.target.value as T)}
    >
      {options.map(([v, text]) => (
        <option key={v} value={v}>
          {text}
        </option>
      ))}
    </select>
  );
}

/** "Saving…", "Saved" or an error, quietly. */
export function SaveState({ status, error }: { status: "idle" | "saving" | "saved" | "error"; error?: string }) {
  if (status === "error" || error)
    return (
      <p role="alert" className="text-sm text-red-400">
        {error || "Could not save."}
      </p>
    );
  return (
    <p role="status" className="text-xs text-muted">
      {status === "saving" ? "Saving…" : status === "saved" ? "Saved. New sessions use these settings." : "Changes save automatically."}
    </p>
  );
}

/** A notice box: success or failure after an action. */
export function Notice({ tone, children }: { tone: "ok" | "error"; children: React.ReactNode }) {
  return (
    <p
      role={tone === "error" ? "alert" : "status"}
      className={
        tone === "error"
          ? "rounded-xl border border-rose-500/40 bg-rose-500/10 px-4 py-3 text-sm text-rose-100"
          : "rounded-xl border border-emerald-500/40 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-100"
      }
    >
      {children}
    </p>
  );
}
