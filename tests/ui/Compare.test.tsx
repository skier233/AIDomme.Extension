import React from "react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "../../../cove/ui/node_modules/@testing-library/react";
import { CompareLab } from "../../extensions/AIDome/ui/Compare";

const api = vi.hoisted(() => ({ fetch: vi.fn() }));
vi.mock("@cove/runtime/api", () => ({ extensionFetch: api.fetch }));

const side = (key: string, title: string, harderChance: number) => ({
  harderChance,
  candidate: { key, group: "Timed", title, description: "", meta: [], points: 4, wins: 0, losses: 0 },
});
const lab = (yours = 0) => ({
  mode: "Stim",
  comparisons: 30 + yours,
  yourComparisons: yours,
  confidence: 0.5,
  pointsPerMinute: 4,
  optimizeNeeds: 0,
  left: side("stim:4:time:60", "Palm for a minute", 0.6),
  right: side("stim:1:time:60", "Figure Eight for a minute", 0.4),
  methods: [{ id: 4, name: "Palm", currentPerMinute: 5, learnedPerMinute: 8, currentPerEdge: null, learnedPerEdge: null, comparisons: 3 }],
  enders: [],
});

beforeEach(() => {
  api.fetch.mockReset();
  api.fetch.mockImplementation(async (path: string, init: any) => {
    if (path.endsWith("/comparisons") && init.method === "POST") return Response.json(lab(1));
    if (path.includes("/comparisons/last") && init.method === "DELETE") return Response.json(lab(0));
    return Response.json(lab(0));
  });
});
afterEach(cleanup);

it("records the one he would rather do as the easier, with how strongly, and can take it back", async () => {
  render(<CompareLab />);
  fireEvent.click(await screen.findByRole("button", { name: "Much rather: Figure Eight for a minute" }));
  await screen.findByText("31 comparisons (1 from this account)");
  const [, init] = api.fetch.mock.calls.find(([path, i]) => path.endsWith("/comparisons") && i.method === "POST")!;
  expect(JSON.parse(init.body)).toEqual({ mode: "Stim", winnerKey: "stim:1:time:60", loserKey: "stim:4:time:60", strength: 3 });
  fireEvent.click(screen.getByRole("button", { name: "Undo my last answer" }));
  await waitFor(() => expect(api.fetch.mock.calls.some(([path, i]) => path.includes("/comparisons/last?mode=Stim") && i.method === "DELETE")).toBe(true));
  expect(screen.getByText("Palm")).toBeTruthy();
});
