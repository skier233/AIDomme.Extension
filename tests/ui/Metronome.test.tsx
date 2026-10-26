import React, { useState } from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { MetronomeAudio } from "../../extensions/AIDomme/ui/MetronomeAudio";
import {
  useMetronome,
  pauseMetronome,
} from "../../extensions/AIDomme/ui/Metronome";

const fetcher = vi.hoisted(() => vi.fn());
vi.mock("@cove/runtime/api", () => ({ extensionFetch: fetcher }));
class Audio {
  static latest: Audio;
  currentTime = 0;
  state = "running";
  destination = {};
  onstatechange: (() => void) | null = null;
  notes: any[] = [];
  gains: any[] = [];
  constructor() {
    Audio.latest = this;
  }
  resume = vi.fn(async () => {
    this.state = "running";
  });
  close = vi.fn(async () => {
    this.state = "closed";
  });
  createGain() {
    const gain = {
      connect: vi.fn(),
      disconnect: vi.fn(),
      gain: {
        setValueAtTime: vi.fn(),
        linearRampToValueAtTime: vi.fn(),
        exponentialRampToValueAtTime: vi.fn(),
      },
    };
    this.gains.push(gain);
    return gain;
  }
  createOscillator() {
    const note = {
      frequency: { value: 0 },
      connect: vi.fn(),
      disconnect: vi.fn(),
      start: vi.fn(),
      stop: vi.fn(),
      onended: null,
    };
    this.notes.push(note);
    return note;
  }
}
beforeEach(() => {
  localStorage.clear();
  vi.stubGlobal("AudioContext", Audio);
  fetcher.mockReset();
  fetcher.mockImplementation(async (path: string) =>
    path.endsWith("release")
      ? new Response(null, { status: 204 })
      : Response.json({ granted: true }),
  );
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function Harness() {
  const metronome = useMetronome(1);
  const [hidden, setHidden] = useState(false);
  return (
    <>
      <button onClick={() => setHidden(!hidden)}>Hide controls</button>
      <button onClick={() => void metronome.toggle()}>Shortcut</button>
      {!hidden && metronome.controls}
    </>
  );
}

it("schedules with the audio clock, skips missed beats, mutes immediately and cancels queued notes", async () => {
  vi.useFakeTimers();
  const engine = new MetronomeAudio(vi.fn());
  await engine.unlock();
  engine.configure(120, 0.5, false);
  engine.start(performance.now() + 10000);
  const context = Audio.latest;
  expect(context.notes[0].start).toHaveBeenCalledWith(0.04);
  context.currentTime = 0.5;
  vi.advanceTimersByTime(25);
  expect(context.notes[1].start).toHaveBeenCalledWith(0.54);
  context.currentTime = 5;
  vi.advanceTimersByTime(25);
  expect(context.notes).toHaveLength(3); // No burst of nine missed beats.
  expect(context.notes[2].start).toHaveBeenCalledWith(5.04);
  context.currentTime = 5.05;
  engine.configure(120, 0.5, true);
  expect(context.gains[0].gain.setValueAtTime).toHaveBeenLastCalledWith(
    0,
    5.05,
  );
  expect(engine.pulse()).toBe(true); // Visual beat survives mute.
  engine.stop();
  expect(context.notes.every((note) => note.stop.mock.calls.length === 2)).toBe(
    true,
  );
  vi.advanceTimersByTime(2000);
  expect(context.notes).toHaveLength(3);
  engine.dispose();
});

it("stops when its lease deadline or browser audio suspension is reached", async () => {
  vi.useFakeTimers();
  const interrupted = vi.fn();
  const engine = new MetronomeAudio(interrupted);
  await engine.unlock();
  engine.start(performance.now() + 200);
  vi.advanceTimersByTime(250);
  expect(engine.running).toBe(false);
  expect(interrupted).toHaveBeenCalledWith(
    expect.stringContaining("Connection lost"),
  );
  engine.start(performance.now() + 10000);
  Audio.latest.state = "suspended";
  Audio.latest.onstatechange?.();
  expect(engine.running).toBe(false);
  expect(interrupted).toHaveBeenLastCalledWith(
    expect.stringContaining("Browser audio paused"),
  );
  engine.dispose();
});

it("keeps playing with controls closed and allows the shortcut to pause", async () => {
  render(<Harness />);
  fireEvent.click(screen.getByText("Start metronome"));
  await screen.findByText("Playing");
  fireEvent.click(screen.getByText("Hide controls"));
  expect(screen.queryByText("Playing")).toBeNull();
  expect(Audio.latest.notes[0].stop).toHaveBeenCalledTimes(1);
  fireEvent.click(screen.getByText("Shortcut"));
  expect(Audio.latest.notes[0].stop).toHaveBeenCalledTimes(2);
  expect(fetcher.mock.calls.some(([path]) => path.endsWith("release"))).toBe(
    true,
  );
});

it("does not start audio if another tab owns the lease", async () => {
  fetcher.mockResolvedValue(Response.json({ granted: false }));
  render(<Harness />);
  fireEvent.click(screen.getByText("Start metronome"));
  await screen.findByText(/another tab or device/);
  expect(Audio.latest.notes).toHaveLength(0);
});

it("stops on session-end signal and tab departure and requires an explicit restart", async () => {
  render(<Harness />);
  fireEvent.click(screen.getByText("Start metronome"));
  await screen.findByText("Playing");
  pauseMetronome();
  await screen.findByText("Paused");
  expect(Audio.latest.notes[0].stop).toHaveBeenCalledTimes(2);
  fireEvent.click(screen.getByText("Start metronome"));
  await screen.findByText("Playing");
  fireEvent(window, new Event("pagehide"));
  await screen.findByText("Paused");
  expect(screen.queryByText("Playing")).toBeNull();
});

it("releases a late grant after a start was canceled without playing", async () => {
  let grant!: (response: Response) => void;
  fetcher.mockImplementation((path: string) =>
    path.endsWith("release")
      ? Promise.resolve(new Response(null, { status: 204 }))
      : new Promise((resolve) => {
          grant = resolve;
        }),
  );
  render(<Harness />);
  fireEvent.click(screen.getByText("Start metronome"));
  await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(1));
  fireEvent.click(screen.getByText("Pause metronome"));
  grant(Response.json({ granted: true }));
  await waitFor(() =>
    expect(
      fetcher.mock.calls.filter(([path]) => path.endsWith("release")),
    ).toHaveLength(2),
  );
  expect(Audio.latest.notes).toHaveLength(0);
  expect(screen.getByText("Paused")).toBeTruthy();
});

it("remembers tempo and volume per account without automatically starting audio", async () => {
  localStorage.setItem(
    "aidomme-metronome:1",
    JSON.stringify({ bpm: 90, volume: 0.6, muted: true }),
  );
  render(<Harness />);
  expect((screen.getByLabelText("Tempo (BPM)") as HTMLInputElement).value).toBe(
    "90",
  );
  expect(
    (screen.getByLabelText("Metronome volume") as HTMLInputElement).value,
  ).toBe("60");
  expect(fetcher).not.toHaveBeenCalled();
  fireEvent.change(screen.getByLabelText("Tempo (BPM)"), {
    target: { value: "105" },
  });
  expect(JSON.parse(localStorage.getItem("aidomme-metronome:1")!).bpm).toBe(
    105,
  );
  expect(screen.getByText("Unmute")).toBeTruthy();
  expect(screen.getByText("Paused")).toBeTruthy();
});

it("reschedules queued notes at a changed tempo without retaining the old rhythm", async () => {
  const engine = new MetronomeAudio(vi.fn());
  await engine.unlock();
  engine.start(performance.now() + 10000);
  const first = Audio.latest.notes[0];
  Audio.latest.currentTime = 0.02;
  engine.configure(180, 0.3, false);
  expect(first.stop).toHaveBeenCalledTimes(2);
  expect(Audio.latest.notes[1].start).toHaveBeenCalledWith(0.06);
  expect(engine.running).toBe(true);
  engine.dispose();
});

function FollowHarness({ bpm }: { bpm: number }) {
  const metronome = useMetronome(1, bpm);
  return <>{metronome.controls}</>;
}

it("follows the domme's tempo without a tempo control, and falls silent when she sets none", async () => {
  const view = render(<FollowHarness bpm={150} />);
  expect(screen.queryByLabelText("Tempo (BPM)")).toBeNull();
  expect(screen.getByText("Her pace: 150 beats per minute.")).toBeTruthy();
  fireEvent.click(screen.getByText("Follow her pace"));
  await screen.findByText("Playing");
  const output = Audio.latest.gains[0];
  // Two beats at 150 BPM are 0.4 seconds apart.
  Audio.latest.currentTime = 0.4;
  await waitFor(() => expect(Audio.latest.notes.length).toBeGreaterThanOrEqual(2));
  expect(Audio.latest.notes[1].start.mock.calls[0][0] - Audio.latest.notes[0].start.mock.calls[0][0]).toBeCloseTo(0.4, 5);
  view.rerender(<FollowHarness bpm={0} />);
  expect(screen.getByText("Silent until she sets a pace.")).toBeTruthy();
  expect(output.gain.setValueAtTime).toHaveBeenLastCalledWith(0, expect.any(Number));
});

function AutoHarness({ auto }: { auto: boolean }) {
  const metronome = useMetronome(1, 120, auto);
  return (
    <>
      <button>Somewhere on the page</button>
      {metronome.controls}
    </>
  );
}

it("starts with her pace on the next click during her session, but never undoes the user's own pause", async () => {
  const view = render(<AutoHarness auto={false} />);
  fireEvent.pointerDown(screen.getByText("Somewhere on the page"));
  expect(screen.getByText("Paused")).toBeTruthy();
  view.rerender(<AutoHarness auto />);
  fireEvent.pointerDown(screen.getByText("Somewhere on the page"));
  await screen.findByText("Playing");
  fireEvent.click(screen.getByText("Pause metronome"));
  await screen.findByText("Paused");
  fireEvent.pointerDown(screen.getByText("Somewhere on the page"));
  await new Promise((done) => setTimeout(done, 20));
  expect(screen.getByText("Paused")).toBeTruthy();
});

it("can be told not to start by itself", async () => {
  render(<AutoHarness auto />);
  fireEvent.click(screen.getByLabelText("Start with her pace automatically"));
  fireEvent.pointerDown(screen.getByText("Somewhere on the page"));
  await new Promise((done) => setTimeout(done, 20));
  expect(screen.getByText("Paused")).toBeTruthy();
});
