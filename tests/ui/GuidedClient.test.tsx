import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import {
  GuidedClient,
  guidedClient,
} from "../../extensions/AIDome/ui/GuidedClient";
import {
  GuidedPanel,
  defaultPolicy,
} from "../../extensions/AIDome/ui/SessionControls";
import { CommandCenterPage } from "../../extensions/AIDome/ui/AIDome";
import { CompanionNarrator } from "../../extensions/AIDome/ui/Narrator";

const runtime = vi.hoisted(() => ({
  fetch: vi.fn(),
  navigate: vi.fn(),
  media: null as any,
}));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: runtime.fetch }));
vi.mock("@cove/runtime/components", () => ({
  getActiveMedia: () => runtime.media,
  navigateToUrl: runtime.navigate,
  useActiveMedia: () => null,
  useExtensionKeyboardBindings: () => ({}),
  useRegisterExtensionKeyboardActions: () => {},
  Field: ({ children }: any) => <div>{children}</div>,
  TextInput: () => null,
  NumberInput: () => null,
  SaveButton: () => null,
}));
let snapshot: any;
let clients: GuidedClient[];
let narrators: CompanionNarrator[];
class Audio {
  static contexts: Audio[] = [];
  state = "running";
  currentTime = 0;
  destination = {};
  onstatechange: (() => void) | null = null;
  sources: any[] = [];
  constructor() {
    Audio.contexts.push(this);
  }
  resume = vi.fn(async () => {});
  close = vi.fn(async () => {
    this.state = "closed";
  });
  decodeAudioData = vi.fn(async () => ({ duration: 60 }));
  createBufferSource() {
    const source = {
      buffer: null,
      connect: vi.fn(),
      disconnect: vi.fn(),
      start: vi.fn(),
      stop: vi.fn(),
      onended: null,
    };
    this.sources.push(source);
    return source;
  }
}
const deferred = () => {
  let resolve!: (value: Response) => void;
  const promise = new Promise<Response>((done) => {
    resolve = done;
  });
  return { promise, resolve };
};
function client() {
  const value = new GuidedClient();
  clients.push(value);
  value.start(1);
  return value;
}
async function loaded(value: GuidedClient) {
  await waitFor(() => expect(value.getSnapshot().snapshot?.userId).toBe(1));
}
beforeEach(() => {
  snapshot = {
    userId: 1,
    instanceId: "server",
    revision: 1,
    policyRevision: 1,
    policy: { ...defaultPolicy },
    sessionActive: true,
    online: {
      sessionId: 10,
      stepNumber: 1,
      edgesThisSession: 2,
      isPaused: false,
      currentDirective: { text: "Current instruction", kind: "TimedMethod" },
      remainingSeconds: 60,
    },
    stepEndsAtUtc: new Date(Date.now() + 60000).toISOString(),
    messages: [
      { id: "message1", sender: "Domme", content: "Current instruction" },
    ],
    actions: [{ id: "beg-to-cum", label: "Request permission" }],
    mediaCue: null,
  };
  clients = [];
  narrators = [];
  Audio.contexts = [];
  runtime.fetch.mockReset();
  runtime.navigate.mockReset();
  runtime.media = null;
  localStorage.clear();
  vi.stubGlobal("AudioContext", Audio);
  runtime.fetch.mockImplementation(async (path: string) => {
    if (path === "/api/ext/aidome/state") return Response.json(snapshot);
    if (path.endsWith("release")) return new Response(null, { status: 204 });
    if (path.includes("/companion/")) return Response.json({ granted: true });
    if (path.endsWith("/tracker/state"))
      return Response.json({ preferences: { settingId: "setting" } });
    if (path.includes("/api/videos?"))
      return Response.json({ items: [{ id: 8 }] });
    if (path.endsWith("/tts")) return new Response(new Uint8Array([1, 2]));
    return Response.json({ ...snapshot, revision: 2 });
  });
});
afterEach(() => {
  cleanup();
  guidedClient.stop();
  for (const value of clients) value.stop();
  for (const value of narrators) value.stop();
  vi.unstubAllGlobals();
});

it("shares one polling owner between the full page and floating view and keeps it through view changes", async () => {
  guidedClient.start(1);
  await loaded(guidedClient);
  const view = render(
    <>
      <CommandCenterPage />
      <GuidedPanel compact />
    </>,
  );
  expect(
    screen.getAllByText("Current instruction").length,
  ).toBeGreaterThanOrEqual(2);
  expect(
    runtime.fetch.mock.calls.filter(
      ([path]) => path === "/api/ext/aidome/state",
    ),
  ).toHaveLength(1);
  view.rerender(<GuidedPanel compact />);
  expect(screen.getByText("Current instruction")).toBeTruthy();
  snapshot = {
    ...snapshot,
    revision: 2,
    online: {
      ...snapshot.online,
      currentDirective: { text: "Next instruction" },
    },
  };
  await guidedClient.refresh();
  await screen.findByText("Next instruction");
  expect(
    runtime.fetch.mock.calls.filter(
      ([path]) => path === "/api/ext/aidome/state",
    ),
  ).toHaveLength(2);
});

it("drops a stale poll that finishes after a session mutation", async () => {
  const value = client();
  await loaded(value);
  const old = deferred();
  runtime.fetch.mockImplementation((path: string) =>
    path === "/api/ext/aidome/state"
      ? old.promise
      : Promise.resolve(
          Response.json({
            ...snapshot,
            revision: 3,
            online: { ...snapshot.online, isPaused: true },
          }),
        ),
  );
  const polling = value.refresh();
  await value.control("pause");
  old.resolve(Response.json({ ...snapshot, revision: 2 }));
  await polling;
  expect(value.getSnapshot().snapshot.online.isPaused).toBe(true);
});

it("preserves the original event ID and media when a tracking request is retried after navigation", async () => {
  const value = client();
  await loaded(value);
  runtime.media = { kind: "video", id: 5, positionSeconds: 30 };
  const requests: any[] = [];
  const original = runtime.fetch.getMockImplementation()!;
  runtime.fetch.mockImplementation(async (path: string, options: any) => {
    if (path.endsWith("/tracker/events")) {
      const body = JSON.parse(options.body);
      requests.push(body);
      return requests.length === 1
        ? Response.json({ message: "Lost response" }, { status: 500 })
        : Response.json({ event: body });
    }
    return original(path, options);
  });
  await value.record("edge");
  expect(value.getSnapshot().pending).not.toBeNull();
  runtime.media = { kind: "image", id: 99 };
  await value.retry();
  expect(requests[1]).toEqual(requests[0]);
  expect(requests[1].mediaId).toBe(5);
  expect(value.getSnapshot().pending).toBeNull();
});

it("drops the previous account's in-flight snapshot when the account changes", async () => {
  const old = deferred();
  let calls = 0;
  runtime.fetch.mockImplementation(async () =>
    ++calls === 1 ? old.promise : Response.json({ ...snapshot, userId: 2 }),
  );
  const value = client();
  value.start(2);
  await waitFor(() => expect(value.getSnapshot().snapshot?.userId).toBe(2));
  old.resolve(Response.json({ ...snapshot, revision: 999 }));
  await Promise.resolve();
  await Promise.resolve();
  expect(value.getSnapshot().snapshot.userId).toBe(2);
});

it("directs native Cove media independently of narration and does not navigate in browsing mode", async () => {
  const value = client();
  await loaded(value);
  expect(runtime.navigate).not.toHaveBeenCalled();
  snapshot = {
    ...snapshot,
    revision: 2,
    policy: { ...defaultPolicy, media: "Engine" },
    mediaCue: { kind: "video", token: 1 },
  };
  await value.refresh();
  await value.setDirectedMedia(true);
  await waitFor(() =>
    expect(runtime.navigate).toHaveBeenCalledWith("/video/8"),
  );
  expect(Audio.contexts).toHaveLength(0);
  expect(value.getSnapshot().voiceEnabled).toBe(false);
  expect(
    runtime.fetch.mock.calls.some(([path]) => path.includes("sort=random")),
  ).toBe(true);
});

it("keeps pause available while a command response is pending and rejects its stale snapshot", async () => {
  const value = client();
  await loaded(value);
  const pending = deferred();
  runtime.fetch.mockImplementation((path: string, options: any) => {
    if (JSON.parse(options?.body || "{}").action === "beg-to-cum")
      return pending.promise;
    return Promise.resolve(
      Response.json({
        ...snapshot,
        revision: 3,
        online: { ...snapshot.online, isPaused: true },
      }),
    );
  });
  const command = value.control("beg-to-cum");
  expect(value.getSnapshot().busy).toBe(true);
  await value.control("pause");
  expect(value.getSnapshot().snapshot.online.isPaused).toBe(true);
  pending.resolve(Response.json({ ...snapshot, revision: 2 }));
  await command;
  expect(value.getSnapshot().snapshot.online.isPaused).toBe(true);
});

it("cancels late TTS responses and cannot restart narration after disable", async () => {
  const narrator = new CompanionNarrator(vi.fn(), vi.fn());
  narrators.push(narrator);
  const pending = deferred();
  const original = runtime.fetch.getMockImplementation()!;
  runtime.fetch.mockImplementation((path: string, options: any) =>
    path.endsWith("/tts") ? pending.promise : original(path, options),
  );
  expect(await narrator.start()).toBe(true);
  narrator.enqueue({ id: "one", content: "Old instruction" });
  await waitFor(() =>
    expect(
      runtime.fetch.mock.calls.some(([path]) => path.endsWith("/tts")),
    ).toBe(true),
  );
  narrator.stop();
  pending.resolve(new Response(new Uint8Array([1])));
  await Promise.resolve();
  await Promise.resolve();
  expect(Audio.contexts.flatMap((context) => context.sources)).toHaveLength(0);
  expect(narrator.ownsAudio).toBe(false);
});

it("schedules a native narration cutoff before lease expiry and pauses immediately", async () => {
  const narrator = new CompanionNarrator(vi.fn(), vi.fn());
  narrators.push(narrator);
  await narrator.start();
  narrator.enqueue({ id: "one", content: "Instruction" });
  await waitFor(() => expect(Audio.contexts[0].sources).toHaveLength(1));
  const source = Audio.contexts[0].sources[0];
  expect(source.start).toHaveBeenCalledTimes(1);
  const cutoff = source.stop.mock.calls[0][0];
  expect(cutoff).toBeGreaterThan(0);
  expect(cutoff).toBeLessThanOrEqual(4);
  narrator.stop();
  expect(source.stop).toHaveBeenLastCalledWith();
});

it("does not request speech for an already claimed message", async () => {
  const narrator = new CompanionNarrator(vi.fn(), vi.fn());
  narrators.push(narrator);
  const original = runtime.fetch.getMockImplementation()!;
  runtime.fetch.mockImplementation((path: string, options: any) =>
    path.endsWith("/claim")
      ? Promise.resolve(Response.json({ granted: false }))
      : original(path, options),
  );
  await narrator.start();
  narrator.enqueue({ id: "one", content: "Already spoken" });
  await waitFor(() =>
    expect(
      runtime.fetch.mock.calls.some(([path]) => path.endsWith("/claim")),
    ).toBe(true),
  );
  expect(runtime.fetch.mock.calls.some(([path]) => path.endsWith("/tts"))).toBe(
    false,
  );
});
