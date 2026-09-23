import { useSyncExternalStore } from "react";
import { extensionFetch } from "@cove/runtime/api";
import { getActiveMedia } from "@cove/runtime/components";
import { pauseMetronome } from "./Metronome";
import { CompanionNarrator } from "./Narrator";

export async function guidedApi(
  path: string,
  body?: unknown,
  method = body === undefined ? "GET" : "POST",
  signal?: AbortSignal,
) {
  const response = await extensionFetch(`/api/ext/aidome${path}`, {
    method,
    signal,
    headers: { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  if (/^\s*</.test(text))
    throw new Error(
      "Reload Cove with the updated AI Domme backend to reconnect guidance.",
    );
  let data: any = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    throw new Error("AI Domme returned an invalid response.");
  }
  if (!response.ok)
    throw new Error(
      data?.message ||
        data?.detail ||
        `AI Domme request failed (${response.status}).`,
    );
  return data;
}

/**
 * What is on screen, for her to talk about: "?media=video:12:345.6" (kind, id, seconds), or "" when
 * nothing she can see is playing.
 */
export function screenQuery(): string {
  let observed: ReturnType<typeof getActiveMedia> | undefined;
  try {
    observed = getActiveMedia();
  } catch {
    return "";
  }
  if (!observed || !(observed.id > 0)) return "";
  if (observed.kind !== "video" && observed.kind !== "image") return "";
  const position =
    typeof observed.positionSeconds === "number" && isFinite(observed.positionSeconds)
      ? `:${observed.positionSeconds.toFixed(1)}`
      : "";
  return `?media=${observed.kind}:${observed.id}${position}`;
}

/** A piece of Cove media the domme picked for the page's media window. */
export type DirectedMedia = {
  kind: "video" | "image" | "audio";
  id: number;
  title?: string;
  format?: string;
  videoCodec?: string;
  audioCodec?: string;
  duration?: number;
};

type ClientState = {
  snapshot: any;
  setup: any;
  error: string;
  busy: boolean;
  pending: any;
  voiceEnabled: boolean;
  voiceStatus: string;
  media: DirectedMedia | null;
  mediaStatus: string;
  now: number;
};
const initial = (): ClientState => ({
  snapshot: null,
  setup: null,
  error: "",
  busy: false,
  pending: null,
  voiceEnabled: false,
  voiceStatus: "",
  media: null,
  mediaStatus: "",
  now: Date.now(),
});

const plural = { video: "videos", image: "images", audio: "audios" } as const;

/** One app-wide owner; the page and the companion are subscribers, never pollers or audio owners. */
export class GuidedClient {
  private state = initial();
  private listeners = new Set<() => void>();
  private userId?: number;
  private generation = 0;
  private mutation = 0;
  private controller: AbortController | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private seen = new Set<string>();
  private mediaToken = "";
  private narrator = new CompanionNarrator(
    (status) => this.publish({ voiceStatus: status }),
    () => this.publish({ voiceEnabled: false }),
  );
  private tick: ReturnType<typeof setInterval> | null = null;
  subscribe = (listener: () => void) => {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };
  getSnapshot = () => this.state;
  private publish(patch: Partial<ClientState>) {
    this.state = { ...this.state, ...patch };
    for (const listener of this.listeners) listener();
  }

  start(userId: number) {
    this.stop();
    this.userId = userId;
    const generation = this.generation;
    const poll = async () => {
      await this.refresh();
      if (generation === this.generation)
        this.timer = setTimeout(
          poll,
          this.state.snapshot?.sessionActive ? 1000 : 2000,
        );
    };
    void poll();
    this.tick = setInterval(() => this.publish({ now: Date.now() }), 1000);
    return () => {
      if (generation === this.generation) this.stop();
    };
  }
  stop() {
    this.generation++;
    this.mutation++;
    this.controller?.abort();
    this.controller = null;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    if (this.tick) clearInterval(this.tick);
    this.tick = null;
    this.narrator.stop();
    this.seen.clear();
    this.mediaToken = "";
    this.userId = undefined;
    this.state = initial();
    for (const listener of this.listeners) listener();
  }
  async refresh() {
    const generation = this.generation,
      mutation = this.mutation;
    if (!this.userId || this.controller) return;
    const controller = new AbortController();
    this.controller = controller;
    try {
      // During a session each poll says what is on screen.
      const snapshot = await guidedApi(
        `/state${this.state.snapshot?.sessionActive ? screenQuery() : ""}`,
        undefined,
        "GET",
        controller.signal,
      );
      if (generation !== this.generation || mutation !== this.mutation) return;
      this.apply(snapshot);
      if (!snapshot.sessionActive) {
        const setup = await guidedApi(
          "/setup",
          undefined,
          "GET",
          controller.signal,
        );
        if (generation === this.generation && mutation === this.mutation)
          this.publish({ setup });
      }
    } catch (e) {
      if (
        generation === this.generation &&
        mutation === this.mutation &&
        !controller.signal.aborted
      ) {
        this.narrator.stop();
        this.publish({ error: (e as Error).message, voiceEnabled: false });
      }
    } finally {
      if (this.controller === controller) this.controller = null;
    }
  }
  private apply(snapshot: any) {
    if (!snapshot || snapshot.userId !== this.userId)
      throw new Error("Companion account changed. Reload Cove.");
    const previous = this.state.snapshot;
    if (
      previous?.instanceId === snapshot.instanceId &&
      previous.revision > snapshot.revision
    )
      return;
    const first = !previous || previous.instanceId !== snapshot.instanceId;
    const messages = snapshot.messages || [];
    const fresh = messages.filter(
      (message: any) =>
        !this.seen.has(message.id) && message.sender === "Domme",
    );
    this.seen = new Set(messages.map((message: any) => message.id));
    const session = snapshot.session;
    if (!snapshot.sessionActive || session?.isPaused) {
      this.narrator.stop();
      this.publish({ voiceEnabled: false });
    } else if (!first && this.state.voiceEnabled && fresh.length)
      this.narrator.enqueue(fresh.at(-1));
    this.publish({ snapshot, error: "", now: Date.now() });

    // The domme picks the content only when the contract gives it to her.
    const cue = snapshot.mediaCue;
    if (
      !snapshot.sessionActive ||
      session?.contract?.content !== "Domme" ||
      !cue
    ) {
      if (this.state.media) this.publish({ media: null, mediaStatus: "" });
      this.mediaToken = "";
      return;
    }
    const token = `${snapshot.instanceId}:${session.sessionId}:${cue.token}`;
    if (token !== this.mediaToken) {
      this.mediaToken = token;
      void this.pickMedia(cue.kind, this.generation, token);
    }
  }
  private async pickMedia(kind: string, generation: number, token: string) {
    if (!(kind in plural)) return;
    const collection = plural[kind as keyof typeof plural];
    try {
      const response = await extensionFetch(
        `/api/${collection}?perPage=1&sort=random&seed=${Math.floor(Math.random() * 1e9)}`,
      );
      if (!response.ok)
        throw new Error(`Media selection failed (${response.status}).`);
      const data = await response.json();
      const item = data?.items?.[0] ?? data?.data?.[0];
      if (!(item?.id > 0)) {
        if (generation === this.generation)
          this.publish({
            media: null,
            mediaStatus: `There are no ${collection} in your library for her to choose.`,
          });
        return;
      }
      let media: DirectedMedia = {
        kind: kind as DirectedMedia["kind"],
        id: item.id,
        title: item.title,
      };
      if (kind === "video") {
        const detail = await extensionFetch(`/api/videos/${item.id}`);
        if (!detail.ok)
          throw new Error(`Media selection failed (${detail.status}).`);
        const video = await detail.json();
        const file = video?.files?.[0];
        if (!file) throw new Error("That video has no playable file.");
        media = {
          ...media,
          title: video.title,
          format: file.format,
          videoCodec: file.videoCodec,
          audioCodec: file.audioCodec,
          duration: file.duration,
        };
      }
      if (generation === this.generation && token === this.mediaToken)
        this.publish({ media, mediaStatus: "" });
    } catch (e) {
      if (generation === this.generation)
        this.publish({ mediaStatus: (e as Error).message });
    }
  }
  async mutate(path: string, body?: any, method = "POST") {
    if (!this.userId || this.state.busy) return;
    const generation = this.generation;
    this.mutation++;
    this.publish({ busy: true, error: "" });
    try {
      const response = await guidedApi(path, body, method);
      if (generation === this.generation) {
        if (response?.userId) this.apply(response);
        if (!response?.sessionActive) void this.refresh();
      }
      return response;
    } catch (e) {
      if (generation === this.generation)
        this.publish({ error: (e as Error).message });
    } finally {
      if (generation === this.generation) this.publish({ busy: false });
    }
  }
  /** Session controls: pause, resume, end (debug), done, request-permission, request-rest, game-answer. */
  async control(action: string, answer?: number, retry = false) {
    const session = this.state.snapshot?.session;
    if (action === "pause" || action === "end") {
      pauseMetronome();
      this.narrator.stop();
      this.publish({ voiceEnabled: false });
      if (!session) return;
      // Pause and End stay available while another request is pending.
      const generation = this.generation;
      this.mutation++;
      try {
        const response = await guidedApi("/session/control", {
          eventId: crypto.randomUUID(),
          sessionId: session.sessionId,
          stepNumber: session.stepNumber,
          action,
        });
        if (generation === this.generation) this.apply(response);
      } catch (e) {
        if (generation === this.generation)
          this.publish({ error: (e as Error).message });
      }
      return;
    }
    if (!session || this.state.busy) return;
    if (this.state.pending && !retry) {
      this.publish({
        error: "Resolve the pending request before another session action.",
      });
      return;
    }
    const pending = this.state.pending || {
      path: "/session/control",
      body: {
        eventId: crypto.randomUUID(),
        sessionId: session.sessionId,
        stepNumber: session.stepNumber,
        action,
        answer,
      },
    };
    this.publish({ pending });
    const response = await this.mutate(pending.path, pending.body);
    if (response) this.publish({ pending: null });
  }
  async record(kind: "edge" | "orgasm", retry = false) {
    if (!this.userId || this.state.busy || (this.state.pending && !retry))
      return;
    const observed = getActiveMedia();
    const session = this.state.snapshot?.session;
    const occurredAtUtc = new Date().toISOString();
    const generation = this.generation;
    if (!this.state.pending) {
      this.publish({ busy: true });
      try {
        let tracker = await guidedApi("/tracker/state");
        if (
          tracker.preferences.settingId ===
          "00000000-0000-0000-0000-000000000000"
        ) {
          await guidedApi("/tracker/initialize", {});
          tracker = await guidedApi("/tracker/state");
        }
        if (generation !== this.generation) return;
        this.publish({
          pending: {
            path: "/tracker/events",
            body: {
              id: crypto.randomUUID(),
              settingId: tracker.preferences.settingId,
              kind,
              occurredAtUtc,
              source: "button",
              mediaKind: observed?.kind,
              mediaId: observed?.id,
              positionSeconds: observed?.positionSeconds,
              guidedSessionId: session ? session.sessionId : 0,
              guidedStepNumber: session ? session.stepNumber : undefined,
            },
          },
        });
      } catch (e) {
        if (generation === this.generation)
          this.publish({ error: (e as Error).message });
        return;
      } finally {
        if (generation === this.generation) this.publish({ busy: false });
      }
    }
    const pending = this.state.pending;
    const response = await this.mutate(pending.path, pending.body);
    if (response && generation === this.generation) {
      this.publish({ pending: null });
      window.dispatchEvent(
        new CustomEvent("aidomme:tracker-changed", { detail: response.event }),
      );
      await this.refresh();
    }
  }
  async retry() {
    if (this.state.pending?.path === "/tracker/events")
      await this.record(this.state.pending.body.kind, true);
    else if (this.state.pending)
      await this.control(
        this.state.pending.body.action,
        this.state.pending.body.answer,
        true,
      );
  }
  dismiss() {
    this.publish({
      pending: null,
      error:
        "Request dismissed. It may already have been applied; review the current state.",
    });
    void this.refresh();
  }
  async setVoice(enabled: boolean) {
    if (!enabled) {
      this.narrator.stop();
      this.publish({ voiceEnabled: false });
      return;
    }
    const session = this.state.snapshot?.session;
    if (!session || session.isPaused) {
      this.publish({
        voiceStatus: "Start or resume a session before enabling narration.",
      });
      return;
    }
    const generation = this.generation;
    const enabledAudio = await this.narrator.start();
    if (generation !== this.generation) {
      this.narrator.stop();
      return;
    }
    this.publish({ voiceEnabled: enabledAudio });
    if (enabledAudio && !this.state.snapshot?.session?.isPaused) {
      const latest = this.state.snapshot.messages
        ?.filter((message: any) => message.sender === "Domme")
        .at(-1);
      if (latest) this.narrator.enqueue(latest);
    }
  }
}

export const guidedClient = new GuidedClient();
export function useGuidedSession() {
  return useSyncExternalStore(
    guidedClient.subscribe,
    guidedClient.getSnapshot,
    guidedClient.getSnapshot,
  );
}
/** Seconds left in the current instruction, or -1 when it has no visible countdown. */
export function remainingSeconds(state: ClientState) {
  const session = state.snapshot?.session;
  if (!session || session.remainingSeconds < 0) return -1;
  return !session.isPaused && state.snapshot.stepEndsAtUtc
    ? Math.max(
        0,
        Math.ceil(
          (Date.parse(state.snapshot.stepEndsAtUtc) - state.now) / 1000,
        ),
      )
    : session.remainingSeconds;
}
