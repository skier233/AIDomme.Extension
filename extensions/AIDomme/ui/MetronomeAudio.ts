/** Audio-clock scheduling is independent of React rendering and tracker polling. */
export class MetronomeAudio {
  private context: AudioContext | null = null;
  private output: GainNode | null = null;
  private nodes = new Set<OscillatorNode>();
  private timer: ReturnType<typeof setInterval> | null = null;
  private next = 0;
  private beats: number[] = [];
  private deadline = 0;
  private bpm = 60;
  private volume = 0.3;
  private muted = false;
  running = false;

  constructor(private interrupted: (reason: string) => void) {}

  /** Must be called synchronously from a user gesture, before the lease request. */
  async unlock() {
    if (!this.context || this.context.state === "closed") {
      this.context = new AudioContext();
      this.output = this.context.createGain();
      this.output.connect(this.context.destination);
      this.context.onstatechange = () => {
        if (this.running && this.context?.state !== "running") {
          this.stop();
          this.interrupted("Browser audio paused. Press Start to resume.");
        }
      };
    }
    await this.context.resume();
    if (this.context.state !== "running")
      throw new Error("Audio is unavailable. Press Start again.");
  }

  configure(bpm: number, volume: number, muted: boolean) {
    const changed = bpm !== this.bpm;
    this.bpm = Math.max(20, Math.min(240, Number.isFinite(bpm) ? bpm : 60));
    this.volume = Math.max(0, Math.min(1, volume));
    this.muted = muted;
    if (this.context && this.output)
      this.output.gain.setValueAtTime(
        muted ? 0 : this.volume,
        this.context.currentTime,
      );
    if (changed && this.running && this.context) {
      this.cancelNotes();
      this.next = this.context.currentTime + 0.04;
      this.schedule();
    }
  }

  renew(deadline: number) {
    this.deadline = deadline;
  }

  start(deadline: number) {
    if (!this.context || this.context.state !== "running")
      throw new Error("Press Start to enable audio.");
    this.stop();
    this.deadline = deadline;
    this.running = true;
    this.configure(this.bpm, this.volume, this.muted);
    this.next = this.context.currentTime + 0.04;
    this.schedule();
    this.timer = setInterval(() => this.schedule(), 25);
  }

  private schedule() {
    const context = this.context;
    if (!this.running || !context || !this.output) return;
    if (performance.now() >= this.deadline) {
      this.stop();
      this.interrupted("Connection lost. Press Start to resume the metronome.");
      return;
    }
    const now = context.currentTime;
    // A throttled scheduler skips missed beats instead of playing a catch-up burst.
    if (this.next < now) this.next = now + 0.04;
    const horizon = Math.min(
      now + 0.1,
      now + (this.deadline - performance.now()) / 1000,
    );
    while (this.next < horizon) {
      const time = this.next;
      const oscillator = context.createOscillator();
      const envelope = context.createGain();
      oscillator.frequency.value = 880;
      envelope.gain.setValueAtTime(0, time);
      envelope.gain.linearRampToValueAtTime(0.3, time + 0.002);
      envelope.gain.exponentialRampToValueAtTime(0.001, time + 0.035);
      oscillator.connect(envelope);
      envelope.connect(this.output);
      oscillator.onended = () => {
        this.nodes.delete(oscillator);
        oscillator.disconnect();
        envelope.disconnect();
      };
      this.nodes.add(oscillator);
      oscillator.start(time);
      oscillator.stop(time + 0.04);
      this.beats.push(time);
      this.next += 60 / this.bpm;
    }
  }

  pulse() {
    const now = this.context?.currentTime ?? 0;
    this.beats = this.beats.filter((time) => time >= now - 0.12);
    return this.running && this.beats.some((time) => time <= now);
  }

  private cancelNotes() {
    for (const node of this.nodes) {
      try {
        node.stop();
      } catch {
        /* Already ended. */
      }
    }
    this.nodes.clear();
    this.beats = [];
  }

  stop() {
    this.running = false;
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.cancelNotes();
    if (this.context && this.output)
      this.output.gain.setValueAtTime(0, this.context.currentTime);
  }

  dispose() {
    this.stop();
    if (this.context) {
      this.context.onstatechange = null;
      void this.context.close();
    }
    this.context = null;
  }
}
