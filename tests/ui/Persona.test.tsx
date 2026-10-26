import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { BodyPanel, EndingsPanel, HerPersonaPanel, LengthsPanel, PlanDebug, ProfileLab, TuningLabLinkPanel, dommeSettings } from "../../extensions/AIDomme/ui/Persona";

const runtime = vi.hoisted(() => ({ fetch: vi.fn(), navigate: vi.fn() }));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: runtime.fetch }));
vi.mock("@cove/runtime/components", () => ({ getActiveMedia: () => null, navigateToUrl: runtime.navigate }));

const stored = () => ({
  settings: {
    persona: { harshness: 5, humiliation: 3, manipulation: 5, talkativeness: 5, variance: 2, names: ["pet"] },
    lengths: { length1: 10, length5: 35, length10: 90 },
    endings: { denial: "Normal", ruins: "Sometimes", orgasmEveryDays: 0 },
    profile: { tolerance: "AWhile" },
  },
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
      <BodyPanel />
    </>,
  );
  const harsh = (await screen.findByRole("slider", { name: "Harshness" })) as HTMLInputElement;
  expect(harsh.value).toBe("5");
  fireEvent.change(harsh, { target: { value: "8" } });
  expect((screen.getByRole("combobox", { name: "How long you can stay at the edge" }) as HTMLSelectElement).value).toBe("AWhile");
  fireEvent.change(screen.getByRole("combobox", { name: "How long you can stay at the edge" }), { target: { value: "Briefly" } });
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
  expect(body.profile).toMatchObject({ tolerance: "Briefly" });
  expect(body.segments).toBeUndefined();
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
  expect(runtime.navigate).toHaveBeenCalledWith("/aidomme-tuning");
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
        body: { closeness: 0.82, need: 0.4, control: 0.35, overwhelm: 0.1, pace: 0.9, paceSource: "learned from 4 edges", recovery: 30, recoverySource: "default", tolerance: 45, toleranceSource: "his setting: a while" },
        aims: [
          { purpose: "control", name: "Test control", goal: "his control worn down to 30%", why: "need at 40%", ended: "", share: 0.6, status: "now", written: "", origin: "plan" },
        ],
        segments: [
          { type: "edge-ladder", name: "Edge ladder", share: 0.2, steps: 3, status: "done", origin: "plan", aim: 0, purpose: "Test control", role: "Advancing", written: true, reason: "its turn came up", parameters: "3 to 5 steps", ended: "ran its steps" },
          { type: "torment", name: "Torment", share: 0, steps: 1, status: "now", origin: "failed a challenge", aim: 0, purpose: "Overwhelm", role: "Side", written: false, reason: "he failed a challenge: he pays for it", parameters: "about 1:00; speeds 7 to 9", ended: "" },
        ],
        revisions: ["2.0 min, step 4: changed her aim from Starve to Test control"],
      }}
    />,
  );
  fireEvent.click(screen.getByText("Her plan (debug)"));
  await waitFor(() => expect(screen.getByText(/changed her aim from Starve to Test control/)).toBeTruthy());
  expect(screen.getByText("Denied")).toBeTruthy();
  expect(screen.getByText("(failed a challenge)", { exact: false })).toBeTruthy();
  expect(screen.getByText(/he failed a challenge: he pays for it/)).toBeTruthy();
  expect(screen.getByText("82%")).toBeTruthy();
  expect(screen.getByText(/his control worn down to 30%/)).toBeTruthy();
});

it("shows what she believes about his body, lets him fix a value and forget what she learned", async () => {
  const view = (settings: any, hasLearned = true) => ({
    settings,
    pace: { value: settings.pace ?? 0.85, source: settings.pace ? "set in the lab" : "learned from 6 edges", default: 1 },
    recovery: { value: 31, source: "learned from 4 edges", default: 25 },
    tolerance: { value: 45, source: "his setting: a while", default: 45 },
    levels: [],
    hasLearned,
  });
  runtime.fetch.mockImplementation(async (path: string, options: any) => {
    if (path.endsWith("/profile/learned") && options?.method === "DELETE") return Response.json(view({ tolerance: "AWhile" }, false));
    if (path.endsWith("/profile") && options?.method === "PUT") return Response.json(view(JSON.parse(options.body)));
    if (path.endsWith("/profile")) return Response.json(view({ tolerance: "AWhile" }));
    return new Response(null, { status: 404 });
  });
  render(<ProfileLab />);
  expect(await screen.findByText(/Source: learned from 6 edges/)).toBeTruthy();
  const pace = screen.getByRole("spinbutton", { name: "Set pace" });
  fireEvent.change(pace, { target: { value: "1.2" } });
  fireEvent.blur(pace);
  expect(await screen.findByText(/Source: set in the lab/)).toBeTruthy();
  const put = runtime.fetch.mock.calls.find(([, o]) => o?.method === "PUT")!;
  expect(JSON.parse(put[1].body)).toMatchObject({ tolerance: "AWhile", pace: 1.2 });
  fireEvent.click(screen.getByRole("button", { name: "Forget what she has learned" }));
  expect(await screen.findByText(/Nothing learned yet/)).toBeTruthy();
});
