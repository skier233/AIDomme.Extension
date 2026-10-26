import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { DefinitionSettings } from "../../extensions/AIDomme/ui/Configuration";
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
    expect(calls[1]).toBe("/api/ext/aidomme/configuration/methods/1/override");
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

describe("adding and removing", () => {
  it("adds a method of his own and takes a shared one out of his library", async () => {
    const posted: any[] = [];
    api.fetch.mockImplementation(async (path: string, init: any) => {
      if (init.method === "POST" && path.endsWith("/configuration/methods")) {
        const made = { ...JSON.parse(init.body), id: 41 };
        posted.push(made);
        data.methods.push({ value: made, isOverride: false });
        return Response.json(made);
      }
      if (init.method === "POST" && path.endsWith("/methods/1/remove")) {
        posted.push(path);
        data.methods[0] = { ...data.methods[0], value: { ...data.methods[0].value, removed: true }, isOverride: true };
        return Response.json(data.methods[0].value);
      }
      return Response.json(structuredClone(data));
    });
    render(<DefinitionSettings />);
    fireEvent.click(await screen.findByRole("button", { name: "+ New method" }));
    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Edging glove" } });
    fireEvent.click(screen.getByRole("button", { name: "Add to my methods" }));
    await screen.findByText("Added to your methods.");
    expect(posted[0]).toMatchObject({ name: "Edging glove", id: 41 });
    expect(screen.getByText("Your own")).toBeTruthy();

    fireEvent.click(within(screen.getByRole("navigation", { name: "Definitions" })).getByRole("button", { name: /^Methods*Intensity/ }));
    fireEvent.click(await screen.findByRole("button", { name: "Remove this method" }));
    await screen.findByText("Removed from your methods. She won't use it.");
    expect(posted[1]).toBe("/api/ext/aidomme/configuration/methods/1/remove");
    expect(screen.getByText("Removed")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Put back in my library" })).toBeTruthy();
  });
});
