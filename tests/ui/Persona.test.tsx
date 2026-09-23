import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { HerSettings, PlanDebug } from "../../extensions/AIDome/ui/Persona";

const runtime = vi.hoisted(() => ({ fetch: vi.fn() }));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: runtime.fetch }));
vi.mock("@cove/runtime/components", () => ({ getActiveMedia: () => null }));

const stored = () => ({
  settings: {
    persona: { harshness: 5, humiliation: 3, manipulation: 5, talkativeness: 5, variance: 2, names: ["pet"] },
    lengths: { length1: 10, length5: 35, length10: 90 },
    segments: { torment: { frequency: "Rarely", length: "Shorter" } },
  },
  segments: [
    { id: "torment", name: "Torment", goal: "Grips that hurt.", tags: ["torture"] },
    { id: "edge-ladder", name: "Edge ladder", goal: "One edge at a time.", tags: ["edging"] },
  ],
});

beforeEach(() => {
  runtime.fetch.mockReset();
  runtime.fetch.mockImplementation(async (path: string, options: any) => {
    if (path.endsWith("/domme-settings") && options?.method === "PUT") return Response.json(JSON.parse(options.body));
    if (path.endsWith("/domme-settings")) return Response.json(stored());
    return new Response(null, { status: 404 });
  });
});
afterEach(() => cleanup());

it("edits her persona and segment preferences, and saves them together", async () => {
  render(<HerSettings />);
  const harsh = (await screen.findByRole("slider", { name: "Harshness" })) as HTMLInputElement;
  expect(harsh.value).toBe("5");
  fireEvent.change(harsh, { target: { value: "8" } });
  expect((screen.getByRole("combobox", { name: "Torment: how often" }) as HTMLSelectElement).value).toBe("Rarely");
  fireEvent.change(screen.getByRole("combobox", { name: "Edge ladder: how often" }), { target: { value: "Often" } });
  fireEvent.change(screen.getByRole("textbox", { name: "What she calls you" }), { target: { value: "pet,  toy , " } });
  fireEvent.change(screen.getByRole("slider", { name: "Length 5 in minutes" }), { target: { value: "45" } });
  fireEvent.click(screen.getByRole("button", { name: "Save her settings" }));
  await screen.findByText("Saved. New sessions use these settings.");
  const [, options] = runtime.fetch.mock.calls.find(([, o]) => o?.method === "PUT")!;
  const body = JSON.parse(options.body);
  expect(body.persona).toMatchObject({ harshness: 8, names: ["pet", "toy"] });
  expect(body.lengths).toEqual({ length1: 10, length5: 45, length10: 90 });
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
  render(<HerSettings />);
  fireEvent.change(await screen.findByRole("slider", { name: "Talkativeness" }), { target: { value: "9" } });
  fireEvent.click(screen.getByRole("button", { name: "Save her settings" }));
  await screen.findByRole("alert");
  expect((screen.getByRole("slider", { name: "Talkativeness" }) as HTMLInputElement).value).toBe("9");
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
