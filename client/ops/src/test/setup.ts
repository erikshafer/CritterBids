import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

// Unmount React trees + reset jsdom between tests so component tests don't leak into each other.
// The staff token lives in sessionStorage (M8-S5), so it is reset too.
afterEach(() => {
  cleanup();
  sessionStorage.clear();
});
