import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { EndingsPanel, HerPersonaPanel, LengthsPanel, PlanDebug, SegmentsPanel, TuningLabLinkPanel, dommeSettings } from "../../extensions/AIDome/ui/Persona";

const runtime = vi.hoisted(() => ({ fetch: vi.fn(), navigate: vi.fn() }));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: runtime.fetch }));
vi.mock("@cove/runtime/components", () => ({ getActiveMedia: () => null, navigateToUrl: runtime.navigate }));

const stored = () => ({
  settings: {
    persona: { harshness: 5, humiliation: 3, manipulation: 5, talkativeness: 5, variance: 2, names: ["pet"] },
    lengths: { length1: 10, length5: 35, length10: 90 },
    endings: { denial: "Normal", ruins: "Sometimes", orgasmEveryDays: 0 },
    segments: { torment: { frequency: "Rarely", length: "Shorter" } },
  },
  segments: [
    { id: "torment", name: "Torment", goal: "Grips that hurt.", tags: ["torture"] },
    { id: "edge-ladder", name: "Edge ladder", goal: "One edge at a time.", tags: ["edging"] },
  ],
});

beforeEach(() => {
  dommeSettings.reset();
  runtime.navigate.mockReset();
  runtime.fetch.mockReset();
  runtime.fetch.mockImplementation(async (path: string, options: any) => {
    if (path.endsWith("/domme-settings") && options?.method === "PUT") return Response.json(JSON.parse(options.body));
    if (path.endsWith("/domme-settings")) return Response.json(stored());
    return new Response(null, { status: 404 });
  });
});
afterEach(() => cleanup());

it("saves each panel's edits into one settings object, automatically", async () => {
  render(
    <>
      <HerPersonaPanel />
      <EndingsPanel />
      <LengthsPanel />
      <SegmentsPanel />
    </>,
  );
  const harsh = (await screen.findByRole("slider", { name: "Harshness" })) as HTMLInputElement;
  expect(harsh.value).toBe("5");
  fireEvent.change(harsh, { target: { value: "8" } });
  expect((screen.getByRole("combobox", { name: "Torment: how often" }) as HTMLSelectElement).value).toBe("Rarely");
  fireEvent.change(screen.getByRole("combobox", { name: "Edge ladder: how often" }), { target: { value: "Often" } });
  const names = screen.getByRole("textbox", { name: "What she calls you" });
  fireEvent.change(names, { target: { value: "pet,  toy , " } });
  fireEvent.blur(names);
  fireEvent.change(screen.getByRole("slider", { name: "Length 5 in minutes" }), { target: { value: "45" } });
  fireEvent.change(screen.getByRole("combobox", { name: "Ruined orgasms" }), { target: { value: "PunishmentOnly" } });
  fireEvent.change(screen.getByRole("combobox", { name: "Denial at the end of a session" }), { target: { value: "Mostly" } });
  fireEvent.change(screen.getByRole("slider", { name: "Days between orgasms" }), { target: { value: "10" } });
  await waitFor(() => expect(screen.getAllByText("Saved. New sessions use these settings.").length).toBeGreaterThan(0), { timeout: 3000 });
  const puts = runtime.fetch.mock.calls.filter(([, o]) => o?.method === "PUT");
  // One save for the burst of edits, with every panel's change in it.
  expect(puts).toHaveLength(1);
  const body = JSON.parse(puts[0][1].body);
  expect(body.persona).toMatchObject({ harshness: 8, names: ["pet", "toy"] });
  expect(body.lengths).toEqual({ length1: 10, length5: 45, length10: 90 });
  expect(body.endings).toEqual({ denial: "Mostly", ruins: "PunishmentOnly", orgasmEveryDays: 10 });
  expect(body.segments).toMatchObject({
    torment: { frequency: "Rarely", length: "Shorter" },
    "edge-ladder": { frequency: "Often", length: "Normal" },
  });
});

it("keeps the edit and shows her refusal when saving fails", async () => {
  runtime.fetch.mockImplementation(async (path: string, options: any) =>
    options?.method === "PUT"
      ? Response.json({ message: "Persona settings run from 0 to 10." }, { status: 400 })
      : Response.json(stored()),
  );
  render(<HerPersonaPanel />);
  fireEvent.change(await screen.findByRole("slider", { name: "Talkativeness" }), { target: { value: "9" } });
  await screen.findByRole("alert", {}, { timeout: 3000 });
  expect(screen.getByRole("alert").textContent).toContain("Persona settings run from 0 to 10.");
  expect((screen.getByRole("slider", { name: "Talkativeness" }) as HTMLInputElement).value).toBe("9");
});

it("links to the tuning lab", async () => {
  render(<TuningLabLinkPanel />);
  fireEvent.click(screen.getByRole("button", { name: "Open the tuning lab" }));
  expect(runtime.navigate).toHaveBeenCalledWith("/aidome-tuning");
});

it("shows her plan, the ending she leans toward and how the plan changed", async () => {
  render(
    <PlanDebug
      plan={{
        mood: "Cruel",
        moodCause: "Pestering",
        annoyance: 0.4,
        points: 30,
        budget: 72,
        persona: { harshness: 7, humiliation: 3, manipulation: 6, talkativeness: 5 },
        ending: "Denied",
        endingReason: "odds 20%",
        presentation: "Lie",
        segments: [
          { type: "edge-ladder", name: "Edge ladder", share: 0.2, steps: 3, status: "done", origin: "plan" },
          { type: "torment", name: "Torment", share: 0.15, steps: 1, status: "now", origin: "annoyed" },
        ],
        revisions: ["2.0 min, step 4: annoyed (Pestering): stroke-steady became torment"],
      }}
    />,
  );
  fireEvent.click(screen.getByText("Her plan (debug)"));
  await waitFor(() => expect(screen.getByText(/stroke-steady became torment/)).toBeTruthy());
  expect(screen.getByText("Denied")).toBeTruthy();
  expect(screen.getByText("(annoyed)", { exact: false })).toBeTruthy();
});
