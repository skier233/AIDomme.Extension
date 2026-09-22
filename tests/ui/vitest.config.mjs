import { defineConfig } from "../../../cove/ui/node_modules/vitest/dist/config.js";
import path from "node:path";
import { fileURLToPath } from "node:url";
const here = path.dirname(fileURLToPath(import.meta.url));
const host = path.resolve(here, "../../../cove/ui");
export default defineConfig({
  root: path.resolve(here, "../.."),
  resolve: {
    alias: {
      "@cove/runtime/api": path.join(here, "api-stub.ts"),
      "@cove/runtime/components": path.join(
        host,
        "src/components/extension-shared.ts",
      ),
      "@tanstack/react-query": path.join(
        host,
        "node_modules/@tanstack/react-query",
      ),
      react: path.join(host, "node_modules/react"),
      "react-dom": path.join(host, "node_modules/react-dom"),
    },
    dedupe: ["react", "react-dom", "@tanstack/react-query"],
  },
  test: { environment: "jsdom", include: ["tests/ui/*.test.tsx"] },
});
