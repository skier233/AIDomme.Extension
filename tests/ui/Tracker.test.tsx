import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TrackerCompanion } from "../../extensions/AIDome/ui/Tracker";

const runtime = vi.hoisted(() => ({
  fetch: vi.fn(),
  media: null as any,
  actions: [] as any[],
}));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: runtime.fetch }));
vi.mock("../../extensions/AIDome/ui/GuidedClient", () => ({ guidedClient: { start: () => () => {}, refresh: () => Promise.resolve(), control: () => Promise.resolve(), getSnapshot: () => ({ snapshot: null }) } }));
vi.mock("../../extensions/AIDome/ui/SessionControls", () => ({ GuidedPanel: () => null }));
vi.mock("@cove/runtime/components", () => ({
  getActiveMedia: () => runtime.media,
  useActiveMedia: () => runtime.media,
  useExtensionKeyboardBindings: () => ({
    "tracker-edge": ["Alt+E"],
    "tracker-orgasm": ["Alt+O"],
    "tracker-toggle": ["Alt+D"],
  }),
  useRegisterExtensionKeyboardActions: (_id: string, actions: any[]) => {
    runtime.actions = actions;
  },
}));

const preferences = {
  userId: 1,
  autoLike: true,
  edgeWeight: 1,
  orgasmWeight: 3,
  settingId: "a-session",
};
let snapshot: any;
let requests: any[];
beforeEach(() => {
  runtime.fetch.mockReset();
  runtime.media = null;
  runtime.actions = [];
  localStorage.clear();
  snapshot = {
    preferences: { ...preferences },
    edges: 0,
    orgasms: 0,
    edgesSinceLastOrgasm: 0,
    recent: [],
  };
  requests = [];
  runtime.fetch.mockImplementation(async (path: string, init: any) => {
    if (path.endsWith("/state")) return Response.json(snapshot);
    const body = init.body ? JSON.parse(init.body) : null;
    requests.push({ path, body });
    if (path.endsWith("/events")) {
      const event = {
        ...body,
        receivedAtUtc: body.occurredAtUtc,
        likeWeight: 3,
      };
      snapshot = {
        ...snapshot,
        edges: snapshot.edges + (body.kind === "edge" ? 1 : 0),
        orgasms: snapshot.orgasms + (body.kind === "orgasm" ? 1 : 0),
        recent: [event],
      };
      return Response.json({ event, snapshot });
    }
    return Response.json(preferences);
  });
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  delete (document as any).fullscreenElement;
});

async function mount() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const result = render(
    <QueryClientProvider client={client}>
      <TrackerCompanion />
    </QueryClientProvider>,
  );
  await screen.findByRole("button", { name: "Orgasm" });
  return result;
}

describe("tracker companion", () => {
  it("explains HTML responses and clears connection errors after recovery", async () => {
    const original = runtime.fetch.getMockImplementation()!;
    let returnHtml = false;
    runtime.fetch.mockImplementation(async (path: string, init: any) => {
      if (returnHtml && path.endsWith("/state"))
        return new Response("<!doctype html><html></html>", {
          headers: { "Content-Type": "text/html" },
        });
      return original(path, init);
    });
    await mount();
    returnHtml = true;
    fireEvent.focus(window);
    expect(await screen.findByRole("alert")).toHaveProperty("textContent", expect.stringContaining("web page instead of tracker data"));
    returnHtml = false;
    fireEvent.focus(window);
    await waitFor(() => expect(screen.queryByRole("alert")).toBeNull());
  });

  it("closes every floating control and reopens through the page action or Cove shortcut", async () => {
    await mount();
    fireEvent.click(screen.getByRole("button", { name: "Close tracker" }));
    expect(document.querySelector(".aidomme-tracker")).toBeNull();
    expect(JSON.parse(localStorage.getItem("aidomme-tracker-ui:1")!).closed).toBe(true);
    runtime.actions.find((x) => x.id === "tracker-edge").action();
    await waitFor(() => expect(requests.length).toBe(1));
    expect(document.querySelector(".aidomme-tracker")).toBeNull();
    fireEvent(window, new Event("aidomme:open-tracker"));
    await screen.findByRole("button", { name: "Close tracker" });
    fireEvent.click(screen.getByRole("button", { name: "Close tracker" }));
    runtime.actions.find((x) => x.id === "tracker-toggle").action();
    await screen.findByRole("button", { name: "Orgasm" });
    expect(JSON.parse(localStorage.getItem("aidomme-tracker-ui:1")!).closed).toBe(false);
  });

  it("restores saved placement inside the current viewport", async () => {
    localStorage.setItem("aidomme-tracker-ui:1", JSON.stringify({ position: { x: 5000, y: 5000 } }));
    await mount();
    await waitFor(() => {
      const panel = document.querySelector(".aidomme-tracker") as HTMLElement;
      expect(Number.parseFloat(panel.style.left)).toBeLessThan(window.innerWidth);
      expect(Number.parseFloat(panel.style.top)).toBeLessThan(window.innerHeight);
    });
  });

  it("uses Cove hotkey registrations and keeps them active while hidden", async () => {
    await mount();
    expect(runtime.actions.map((x) => x.id)).toEqual([
      "tracker-edge",
      "tracker-orgasm",
      "tracker-toggle",
      "metronome-toggle",
      "session-complete",
      "session-permission",
      "session-stop-request",
      "session-pause",
    ]);
    fireEvent.click(screen.getByRole("button", { name: "Hide tracker" }));
    await screen.findByRole("button", { name: "AI Domme tracker" });
    runtime.media = { kind: "video", id: 12, positionSeconds: 42 };
    runtime.actions.find((x) => x.id === "tracker-edge").action();
    await waitFor(() => expect(requests.length).toBe(1));
    expect(requests[0].body).toMatchObject({
      kind: "edge",
      source: "hotkey",
      mediaKind: "video",
      mediaId: 12,
      positionSeconds: 42,
    });
  });

  it("retries with the original event ID and media when browsing moves on", async () => {
    await mount();
    runtime.media = { kind: "image", id: 10 };
    const original = runtime.fetch.getMockImplementation()!;
    let fail = true;
    runtime.fetch.mockImplementation(async (path: string, init: any) => {
      if (path.endsWith("/events") && fail) {
        fail = false;
        requests.push({ path, body: JSON.parse(init.body) });
        throw new Error("Disconnected");
      }
      return original(path, init);
    });
    fireEvent.click(screen.getByRole("button", { name: "Orgasm" }));
    await screen.findByRole("button", { name: "Retry same event" });
    runtime.media = { kind: "image", id: 99 };
    fireEvent.click(screen.getByRole("button", { name: "Retry same event" }));
    await waitFor(() => expect(requests.length).toBe(2));
    expect(requests[1].body).toEqual(requests[0].body);
    expect(requests[1].body.mediaId).toBe(10);
  });

  it("records without attribution when no media is active", async () => {
    await mount();
    fireEvent.click(screen.getByRole("button", { name: "Edge" }));
    await waitFor(() => expect(requests.length).toBe(1));
    expect(requests[0].body.mediaId).toBeUndefined();
  });

  it("moves the controls into the fullscreen subtree", async () => {
    const result = await mount();
    const fullscreen = document.createElement("div");
    document.body.appendChild(fullscreen);
    Object.defineProperty(document, "fullscreenElement", {
      configurable: true,
      value: fullscreen,
    });
    fireEvent(document, new Event("fullscreenchange"));
    await waitFor(() =>
      expect(fullscreen.querySelector(".aidomme-tracker")).not.toBeNull(),
    );
    expect(result.container.querySelector(".aidomme-tracker")).toBeNull();
    result.unmount();
    fullscreen.remove();
  });
});
