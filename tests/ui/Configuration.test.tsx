import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { DefinitionSettings } from "../../extensions/AIDome/ui/Configuration";
const api = vi.hoisted(() => ({ fetch: vi.fn() }));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: api.fetch }));
let data: any;
let calls: any[];
const method = {
  id: 1,
  name: "Method",
  description: "Instructions",
  intensity: 3,
  tease: 2,
  tortLevel: 1,
  closeness: 4,
  requiresLube: false,
  toyCategory: "None",
  timePointMultiplier: 0.123,
  edgePointMultiplier: 2,
};
beforeEach(() => {
  calls = [];
  data = {
    methods: [
      { value: { ...method }, sharedDefault: { ...method }, isOverride: false },
    ],
    enders: [],
    ratings: [],
    comparisons: [],
  };
  api.fetch.mockReset();
  api.fetch.mockImplementation(async (path: string, init: any) => {
    if (init.method === "PUT") {
      const value = JSON.parse(init.body);
      calls.push(value);
      data.methods[0] = { ...data.methods[0], value, isOverride: true };
      return Response.json(value);
    }
    if (init.method === "DELETE") {
      data.methods[0] = {
        ...data.methods[0],
        value: { ...method },
        isOverride: false,
      };
      calls.push(path);
      return Response.json(method);
    }
    return Response.json(structuredClone(data));
  });
});
afterEach(cleanup);
describe("personal definitions", () => {
  it("saves edits without losing tuning and restores the shared default", async () => {
    render(<DefinitionSettings />);
    await screen.findByLabelText("Intensity");
    fireEvent.change(screen.getByLabelText("Intensity"), {
      target: { value: "8" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save personal override" }),
    );
    await screen.findByText("Your personal override");
    expect(calls[0]).toMatchObject({
      intensity: 8,
      timePointMultiplier: 0.123,
      edgePointMultiplier: 2,
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Restore shared default" }),
    );
    await screen.findByText("Shared default restored.");
    await waitFor(() => expect((screen.getByLabelText("Intensity") as HTMLInputElement).value).toBe("3"));
    expect(calls[1]).toBe("/api/ext/aidome/configuration/methods/1/override");
  });
  it("keeps an unsaved edit when saving fails", async () => {
    const original = api.fetch.getMockImplementation()!;
    api.fetch.mockImplementation((path: string, init: any) =>
      init.method === "PUT"
        ? Response.json({ message: "Save failed" }, { status: 500 })
        : original(path, init),
    );
    render(<DefinitionSettings />);
    await screen.findByLabelText("Intensity");
    fireEvent.change(screen.getByLabelText("Intensity"), {
      target: { value: "8" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save personal override" }),
    );
    await waitFor(() =>
      expect(screen.getByRole("alert").textContent).toBe("Save failed"),
    );
    expect((screen.getByLabelText("Intensity") as HTMLInputElement).value).toBe(
      "8",
    );
    expect(screen.getByText("Using shared default")).toBeTruthy();
  });
});
