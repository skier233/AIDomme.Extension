import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "../../../cove/ui/node_modules/@testing-library/react";
import { CommitmentPanel } from "../../extensions/AIDome/ui/Commitments";
const api = vi.hoisted(() => ({ fetch: vi.fn() }));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: api.fetch }));

describe("commitment panel", () => {
  beforeEach(() => api.fetch.mockReset());
  afterEach(cleanup);
  it("requires the explicit accept action and sends an idempotency event", async () => {
    const offer = {
      id: crypto.randomUUID(),
      name: "Example",
      terms: "Agreed terms",
      unit: "items",
      target: 2,
      progress: 0,
      status: "Proposed",
    };
    let state: any = { offers: [offer], commitments: [], reports: [] };
    api.fetch.mockImplementation(async (_path: string, init?: RequestInit) => {
      if (!init) return Response.json(state);
      const body = JSON.parse(String(init.body));
      expect(body.action).toBe("accept");
      expect(body.targetId).toBe(offer.id);
      expect(body.expectedRevision).toBeUndefined();
      expect(body.eventId).toMatch(/^[0-9a-f-]{36}$/i);
      state = {
        ...state,
        offers: [{ ...offer, status: "Accepted" }],
        commitments: [
          {
            ...offer,
            id: crypto.randomUUID(),
            offerId: offer.id,
            status: "Active",
          },
        ],
      };
      return Response.json(state);
    });
    render(<CommitmentPanel />);
    expect(await screen.findByText("Agreed terms")).toBeTruthy();
    expect(screen.queryByRole("progressbar")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Explicitly accept" }));
    await waitFor(() => expect(screen.getByRole("progressbar")).toBeTruthy());
    expect(api.fetch).toHaveBeenCalledTimes(2);
  });
});
