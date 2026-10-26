import { extensionFetch } from "@cove/runtime/api";
import { CompanionLease, companionPost } from "./CompanionLease";

export class CompanionNarrator {
  private context: AudioContext | null = null;
  private source: AudioBufferSourceNode | null = null;
  private controller: AbortController | null = null;
  private generation = 0;
  private queue: any[] = [];
  private pumping = false;
  private cutoff: ReturnType<typeof setInterval> | null = null;
  private lease: CompanionLease;
  constructor(
    private status: (message: string) => void,
    private disabled: () => void,
  ) {
    this.lease = new CompanionLease("audio", (reason) => {
      this.stop();
      this.status(reason);
      this.disabled();
    });
  }
  get ownsAudio() {
    return this.lease.owns;
  }
  async start() {
    this.stop();
    const generation = this.generation;
    try {
      // AudioContext is created/resumed inside the initiating gesture, before any request.
      this.context = new AudioContext();
      this.context.onstatechange = () => {
        if (
          this.context?.state !== "running" ||
          (this.source && !this.ownsAudio)
        ) {
          this.stop();
          this.disabled();
          this.status("Browser audio paused. Enable narration again.");
        }
      };
      await this.context.resume();
      if (generation !== this.generation) return false;
      if (!(await this.lease.start())) {
        this.stop();
        return false;
      }
      if (generation !== this.generation) {
        this.stop();
        return false;
      }
      this.status("Narration enabled in this browser.");
      return true;
    } catch (e) {
      if (generation === this.generation) {
        this.stop();
        this.status((e as Error).message);
      }
      return false;
    }
  }
  enqueue(message: any) {
    if (!this.ownsAudio || !message?.id || !message?.content) return;
    // A newer directive replaces obsolete pending/in-flight narration rather than building a backlog.
    this.cancelPlayback();
    this.queue = [message];
    void this.pump();
  }
  private cancelPlayback() {
    this.generation++;
    this.controller?.abort();
    this.controller = null;
    if (this.cutoff) clearInterval(this.cutoff);
    this.cutoff = null;
    if (this.source) {
      try {
        this.source.stop();
      } catch {}
      this.source.disconnect();
      this.source = null;
    }
    this.queue = [];
    this.pumping = false;
  }
  private async pump() {
    if (this.pumping || !this.ownsAudio || !this.context) return;
    this.pumping = true;
    const generation = this.generation,
      context = this.context;
    const controller = new AbortController();
    this.controller = controller;
    const message = this.queue.shift();
    try {
      const claim = await companionPost("audio/claim", {
        clientId: this.lease.clientId,
        messageId: message.id,
      });
      if (!claim.granted || generation !== this.generation || !this.ownsAudio)
        return;
      const response = await extensionFetch("/api/ext/aidomme/tts", {
        method: "POST",
        signal: controller.signal,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: message.content }),
      });
      if (!response.ok)
        throw new Error(
          "Voice server unavailable. Text instructions remain available.",
        );
      const buffer = await context.decodeAudioData(
        await response.arrayBuffer(),
      );
      if (
        generation !== this.generation ||
        !this.ownsAudio ||
        context.state !== "running"
      )
        return;
      const source = context.createBufferSource();
      source.buffer = buffer;
      source.connect(context.destination);
      this.source = source;
      source.onended = () => {
        source.disconnect();
        if (this.source === source) {
          this.source = null;
          if (this.cutoff) clearInterval(this.cutoff);
          this.cutoff = null;
        }
      };
      source.start();
      const cutoff = () => {
        if (this.source !== source) return;
        // Native audio-clock cutoff remains effective even if JavaScript is throttled/frozen.
        source.stop(context.currentTime + this.lease.remainingSeconds);
      };
      cutoff();
      this.cutoff = setInterval(cutoff, 100);
    } catch (e) {
      if (generation === this.generation && !controller.signal.aborted)
        this.status((e as Error).message);
    } finally {
      if (generation === this.generation) {
        this.pumping = false;
        this.controller = null;
      }
    }
  }
  stop() {
    this.cancelPlayback();
    this.lease.stop();
    if (this.context) {
      this.context.onstatechange = null;
      if (this.context.state !== "closed")
        void this.context.close().catch(() => {});
    }
    this.context = null;
  }
}
