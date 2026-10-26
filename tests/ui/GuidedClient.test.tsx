import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import {
  GuidedClient,
  guidedClient,
} from "../../extensions/AIDomme/ui/GuidedClient";
import { GuidedPanel } from "../../extensions/AIDomme/ui/SessionControls";
import { CommandCenterPage } from "../../extensions/AIDomme/ui/AIDomme";
import { CompanionNarrator } from "../../extensions/AIDomme/ui/Narrator";

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
  VideoPlayer: ({ videoId }: any) => <div>Player for video {videoId}</div>,
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
const contract = (content = "Domme", pacing = "Domme") => ({
  version: 1,
  mode: "FullGuided",
  content,
  methods: "Domme",
  pacing,
  edges: pacing,
  games: true,
  preferences: { intensity: 5, length: 5, denial: 5, mean: 5 },
  limits: { toys: [], anal: false, bondage: false, cei: false, selfOral: false, allowedMethodIds: [] },
  punishment: false,
  debug: false,
  gamesOnly: false,
  canChangeDuringSession: false,
});
const setup = {
  defaults: { intensity: 5, length: 5, denial: 5, mean: 5 },
  ledger: { commitments: [], entries: [], followUps: [] },
  messages: [],
  modes: [
    { mode: "FullGuided", name: "Fully guided", description: "She decides everything.", contract: contract() },
    { mode: "OwnMethods", name: "Your hands, her pace", description: "You choose how.", contract: { ...contract("User"), mode: "OwnMethods", methods: "User" } },
  ],
};
beforeEach(() => {
  snapshot = {
    userId: 1,
    instanceId: "server",
    revision: 1,
    sessionActive: true,
    session: {
      sessionId: 10,
      stepNumber: 1,
      edgesThisSession: 2,
      isPaused: false,
      isDebugSession: false,
      mode: "FullGuided",
      contract: contract("User"),
      contractRevision: 1,
      currentDirective: { text: "Current instruction", kind: "TimedMethod" },
      remainingSeconds: 60,
      canRequestPermission: true,
      canRequestRest: true,
      canComplete: false,
      tempo: 90,
      game: null,
    },
    stepEndsAtUtc: new Date(Date.now() + 60000).toISOString(),
    messages: [
      { id: "message1", sender: "Domme", content: "Current instruction" },
    ],
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
    if (path === "/api/ext/aidome/state" || path.startsWith("/api/ext/aidome/state?")) return Response.json(snapshot);
    if (path === "/api/ext/aidome/setup") return Response.json(setup);
    if (path === "/api/ext/aidome/configuration") return Response.json({ methods: [] });
    if (path.endsWith("release")) return new Response(null, { status: 204 });
    if (path.includes("/companion/")) return Response.json({ granted: true });
    if (path.endsWith("/tracker/state"))
      return Response.json({ preferences: { settingId: "setting" } });
    if (path.includes("/api/videos?"))
      return Response.json({ items: [{ id: 8 }] });
    if (path === "/api/videos/8")
      return Response.json({ id: 8, title: "Chosen", files: [{ format: "mp4", duration: 90 }] });
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
      <GuidedPanel />
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
  view.rerender(<GuidedPanel />);
  expect(screen.getByText("Current instruction")).toBeTruthy();
  snapshot = {
    ...snapshot,
    revision: 2,
    session: {
      ...snapshot.session,
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
            session: { ...snapshot.session, isPaused: true },
          }),
        ),
  );
  const polling = value.refresh();
  await value.control("pause");
  old.resolve(Response.json({ ...snapshot, revision: 2 }));
  await polling;
  expect(value.getSnapshot().snapshot.session.isPaused).toBe(true);
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
  expect(requests[1].guidedSessionId).toBe(10);
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

it("shows the domme's content in the page, and never picks content the user owns", async () => {
  const value = client();
  await loaded(value);
  snapshot = { ...snapshot, revision: 2, mediaCue: { kind: "video", token: 1 } };
  await value.refresh();
  expect(runtime.fetch.mock.calls.some(([path]) => path.includes("sort=random"))).toBe(false);
  expect(value.getSnapshot().media).toBeNull();
  snapshot = {
    ...snapshot,
    revision: 3,
    session: { ...snapshot.session, contract: contract("Domme") },
  };
  await value.refresh();
  await waitFor(() => expect(value.getSnapshot().media?.id).toBe(8));
  expect(value.getSnapshot().media?.format).toBe("mp4");
  expect(runtime.navigate).not.toHaveBeenCalled();
  expect(Audio.contexts).toHaveLength(0);
});

it("keeps pause available while a command response is pending and rejects its stale snapshot", async () => {
  const value = client();
  await loaded(value);
  const pending = deferred();
  runtime.fetch.mockImplementation((path: string, options: any) => {
    if (JSON.parse(options?.body || "{}").action === "request-permission")
      return pending.promise;
    return Promise.resolve(
      Response.json({
        ...snapshot,
        revision: 3,
        session: { ...snapshot.session, isPaused: true },
      }),
    );
  });
  const command = value.control("request-permission");
  expect(value.getSnapshot().busy).toBe(true);
  await value.control("pause");
  expect(value.getSnapshot().snapshot.session.isPaused).toBe(true);
  pending.resolve(Response.json({ ...snapshot, revision: 2 }));
  await command;
  expect(value.getSnapshot().snapshot.session.isPaused).toBe(true);
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

it("tells her what is on screen with each poll, but only during a session", async () => {
  runtime.media = { kind: "video", id: 12, positionSeconds: 345.64 };
  guidedClient.start(1);
  await loaded(guidedClient);
  await guidedClient.refresh();
  expect(runtime.fetch.mock.calls.map(([path]) => path)).toContain("/api/ext/aidome/state?media=video:12:345.6");
  runtime.fetch.mockClear();
  snapshot = { ...snapshot, revision: 3, sessionActive: false, session: null };
  await guidedClient.refresh();
  await guidedClient.refresh();
  expect(runtime.fetch.mock.calls.map(([path]) => path).filter((path) => path.includes("media="))).toHaveLength(1);
  runtime.media = { kind: "audio", id: 3 };
  expect((await import("../../extensions/AIDomme/ui/GuidedClient")).screenQuery()).toBe("");
});

it("offers pause but not end outside debug sessions", async () => {
  guidedClient.start(1);
  await loaded(guidedClient);
  render(<GuidedPanel />);
  expect(screen.getByRole("button", { name: "Pause" })).toBeTruthy();
  expect(screen.queryByRole("button", { name: "End (debug)" })).toBeNull();
  snapshot = {
    ...snapshot,
    revision: 2,
    session: { ...snapshot.session, isDebugSession: true },
  };
  await guidedClient.refresh();
  await screen.findByRole("button", { name: "End (debug)" });
});

it("shows only the controls the user holds, and answers games with the choices the server offers", async () => {
  guidedClient.start(1);
  await loaded(guidedClient);
  snapshot = {
    ...snapshot,
    revision: 2,
    session: {
      ...snapshot.session,
      canRequestRest: false,
      game: { type: "TwoEnvelopes", name: "Two envelopes", input: "Choices", choices: [{ label: "Envelope 1", value: 1 }, { label: "Envelope 2", value: 2 }] },
    },
  };
  await guidedClient.refresh();
  render(<GuidedPanel />);
  expect(screen.getByRole("button", { name: "Ask to cum" })).toBeTruthy();
  expect(screen.queryByRole("button", { name: "Beg for a rest" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Envelope 2" }));
  await waitFor(() =>
    expect(
      runtime.fetch.mock.calls.some(
        ([path, options]) => path.endsWith("/session/control") && JSON.parse(options.body).answer === 2,
      ),
    ).toBe(true),
  );
  const body = JSON.parse(runtime.fetch.mock.calls.find(([path]) => path.endsWith("/session/control"))![1].body);
  expect(body).toMatchObject({ action: "game-answer", sessionId: 10, stepNumber: 1 });
});

it("starts a session with the chosen mode's authorities and the user's preferences", async () => {
  snapshot = { ...snapshot, sessionActive: false, session: null };
  guidedClient.start(1);
  await waitFor(() => expect(guidedClient.getSnapshot().setup).not.toBeNull());
  render(<CommandCenterPage />);
  fireEvent.click(await screen.findByRole("button", { name: /Your hands, her pace/ }));
  fireEvent.change(screen.getByRole("slider", { name: "Meanness" }), { target: { value: "8" } });
  fireEvent.click(screen.getByRole("button", { name: "Begin" }));
  await waitFor(() =>
    expect(runtime.fetch.mock.calls.some(([path]) => path.endsWith("/session/start"))).toBe(true),
  );
  const body = JSON.parse(runtime.fetch.mock.calls.find(([path]) => path.endsWith("/session/start"))![1].body);
  expect(body.contract).toMatchObject({ content: "User", methods: "User", pacing: "Domme", edges: "Domme" });
  expect(body.contract.preferences).toMatchObject({ mean: 8, intensity: -1 });
});

const bodyOf = (suffix: string) => {
  const call = runtime.fetch.mock.calls.find(([path]) => path.endsWith(suffix));
  return call ? JSON.parse(call[1].body) : undefined;
};

it("asks her questions before anything else, and sends the chosen answer", async () => {
  snapshot = {
    ...snapshot,
    sessionActive: false,
    session: null,
    followUps: [
      {
        id: "q1",
        kind: "UnauthorizedOrgasm",
        question: "You came without my permission. Was it an accident?",
        observed: "An orgasm was recorded.",
        occurredAtUtc: new Date().toISOString(),
        answers: [
          { value: "accident", label: "It was an accident" },
          { value: "intent", label: "I did it on purpose" },
        ],
      },
    ],
  };
  guidedClient.start(1);
  await waitFor(() => expect(guidedClient.getSnapshot().setup).not.toBeNull());
  render(<CommandCenterPage />);
  expect(await screen.findByText("Before anything else")).toBeTruthy();
  expect((screen.getByRole("button", { name: "Begin" }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByRole("button", { name: "It was an accident" }));
  await waitFor(() => expect(bodyOf("/followups/q1/answer")).toEqual({ answer: "accident" }));
});

it("shows her offer with accept and decline, and the debts the session carries", async () => {
  snapshot = {
    ...snapshot,
    session: {
      ...snapshot.session,
      canRequestPermission: false,
      owedEdges: 4,
      noOrgasmUntilUtc: null,
      offer: { id: "o1", buys: "Orgasm", kind: "EdgesNow", text: "Edge 5 more times, then you may cum.", secondsLeft: 40 },
    },
  };
  guidedClient.start(1);
  await loaded(guidedClient);
  render(<GuidedPanel />);
  expect(screen.getByText("Edge 5 more times, then you may cum.")).toBeTruthy();
  expect(screen.getByText("4 owed edges")).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "Accept" }));
  await waitFor(() => expect(bodyOf("/session/control")).toMatchObject({ action: "offer-accept", sessionId: 10 }));
});

it("proposes a structured promise, and accepts her terms", async () => {
  snapshot = { ...snapshot, sessionActive: false, session: null, followUps: [] };
  setup.ledger = {
    commitments: [
      { id: "c1", status: "Pending", response: "Not enough. Make it no orgasm for the next 5 days.", description: "no orgasm for the next 5 days", stakes: [] },
    ],
    entries: [{ id: 1, kind: "Edges", status: "Owed", description: "10 edges before you cum again", reason: "You came without permission." }],
    followUps: [],
  };
  try {
    guidedClient.start(1);
    await waitFor(() => expect(guidedClient.getSnapshot().setup).not.toBeNull());
    render(<CommandCenterPage />);
    expect(await screen.findByText("10 edges before you cum again")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Accept her terms" }));
    await waitFor(() => expect(runtime.fetch.mock.calls.some(([path]) => path.endsWith("/commitments/c1/accept"))).toBe(true));
    fireEvent.click(screen.getByRole("checkbox", { name: "Mean" }));
    fireEvent.click(screen.getByRole("radio", { name: /For/ }));
    fireEvent.click(screen.getByRole("button", { name: "Propose" }));
    await waitFor(() => expect(bodyOf("/commitments/propose")).toBeDefined());
    expect(bodyOf("/commitments/propose").terms).toMatchObject({ scope: "Days", days: 3, noOrgasm: true, mean: true });
  } finally {
    setup.ledger = { commitments: [], entries: [], followUps: [] };
  }
});

it("shows what she said since the current order right under it, newest first to catch the eye", async () => {
  const at = new Date().toISOString();
  snapshot = {
    ...snapshot,
    revision: 5,
    session: { ...snapshot.session, currentDirective: { text: "Edge for me.", kind: "EdgeCount" } },
    messages: [
      { id: "old", sender: "Domme", content: "Earlier order.", kind: "order", timestamp: at },
      { id: "open", sender: "Domme", content: "One edge at a time.", kind: "say", timestamp: at },
      { id: "order", sender: "Domme", content: "Edge for me.", kind: "order", timestamp: at },
      { id: "aside", sender: "Domme", content: "Getting close? Good.", kind: "aside", timestamp: at },
      { id: "count", sender: "Domme", content: "That's 2.", kind: "say", timestamp: at },
      { id: "note", sender: "Manager", content: "A notice.", timestamp: at },
    ],
  };
  guidedClient.start(1);
  await loaded(guidedClient);
  render(<GuidedPanel />);
  const latest = await screen.findByRole("list", { name: "What she said" });
  expect(Array.from(latest.querySelectorAll("li")).map((x) => x.textContent)).toEqual([
    "One edge at a time.",
    "Getting close? Good.",
    "That's 2.",
  ]);
  expect(latest.querySelector("li.is-newest")?.textContent).toBe("That's 2.");
});
