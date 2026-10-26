import { extensionFetch } from "@cove/runtime/api";

export async function companionPost(path: string, body: unknown) {
  const response = await extensionFetch(`/api/ext/aidomme/companion/${path}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok)
    throw new Error(`Companion connection failed (${response.status}).`);
  if (response.status === 204) return null;
  if (
    !(response.headers.get("content-type") || "").includes("application/json")
  )
    throw new Error("Restart Cove with the updated AI Domme backend.");
  return response.json();
}

/** One narration owner per account across tabs and devices. */
export class CompanionLease {
  clientId: string | null = null;
  private generation = 0;
  private timer: ReturnType<typeof setInterval> | null = null;
  private deadline = 0;
  private renewing = false;
  private renewAt = 0;
  constructor(
    private surface: string,
    private lost: (reason: string) => void,
  ) {}
  get owns() {
    return !!this.clientId && performance.now() < this.deadline;
  }
  get remainingSeconds() {
    return Math.max(0, (this.deadline - performance.now()) / 1000);
  }
  private depart = () =>
    this.fail("This tab is away. Enable this browser again when ready.");
  private visibility = () => {
    if (document.hidden) this.depart();
  };
  private fail(reason: string) {
    this.stop();
    this.lost(reason);
  }
  async start() {
    this.stop();
    const generation = this.generation,
      clientId = crypto.randomUUID();
    this.clientId = clientId;
    const sent = performance.now();
    try {
      const response = await companionPost(`${this.surface}/lease`, {
        clientId,
      });
      if (generation !== this.generation) {
        if (response.granted)
          void companionPost(`${this.surface}/release`, { clientId }).catch(
            () => {},
          );
        return false;
      }
      if (!response.granted)
        throw new Error(
          "Another tab or device owns this feature. Disable it there first.",
        );
      if (performance.now() >= sent + 4000)
        throw new Error("Connection was too slow. Try again.");
      this.deadline = sent + 4000;
      this.renewAt = sent + 1500;
      this.renewing = false;
      window.addEventListener("pagehide", this.depart);
      document.addEventListener("visibilitychange", this.visibility);
      this.timer = setInterval(() => {
        if (performance.now() >= this.deadline) {
          this.fail("Ownership expired. Enable this browser again.");
          return;
        }
        if (performance.now() >= this.renewAt && !this.renewing)
          void this.renew(generation, clientId);
      }, 100);
      return true;
    } catch (e) {
      if (generation === this.generation) this.fail((e as Error).message);
      return false;
    }
  }
  private async renew(generation: number, clientId: string) {
    this.renewing = true;
    const sent = performance.now();
    try {
      const response = await companionPost(`${this.surface}/lease`, {
        clientId,
      });
      if (generation !== this.generation) {
        if (response.granted)
          void companionPost(`${this.surface}/release`, { clientId }).catch(
            () => {},
          );
        return;
      }
      if (!response.granted || performance.now() >= sent + 4000)
        throw new Error("Ownership lost. Enable this browser again.");
      this.deadline = sent + 4000;
      this.renewAt = sent + 1500;
    } catch (e) {
      if (generation === this.generation) this.fail((e as Error).message);
    } finally {
      if (generation === this.generation) this.renewing = false;
    }
  }
  stop() {
    this.generation++;
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    window.removeEventListener("pagehide", this.depart);
    document.removeEventListener("visibilitychange", this.visibility);
    const clientId = this.clientId;
    this.clientId = null;
    this.deadline = 0;
    if (clientId)
      void companionPost(`${this.surface}/release`, { clientId }).catch(
        () => {},
      );
  }
}
