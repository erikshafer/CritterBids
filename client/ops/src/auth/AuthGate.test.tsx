import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { AuthGate } from "@/auth/AuthGate";
import { StaffAuthProvider, useStaffAuth } from "@/auth/StaffAuthContext";
import { STAFF_TOKEN_HEADER } from "@/auth/staffApi";

const STORAGE_KEY = "critterbids.staffToken";

function jsonResponse(status: number): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => [],
  } as unknown as Response;
}

function renderGate(children: React.ReactNode = <div data-testid="dashboard" />) {
  return render(
    <StaffAuthProvider>
      <AuthGate>{children}</AuthGate>
    </StaffAuthProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("AuthGate", () => {
  it("renders the gate (not the dashboard) when no token is held", () => {
    renderGate();

    expect(screen.getByPlaceholderText("Enter the staff token")).toBeInTheDocument();
    expect(screen.queryByTestId("dashboard")).not.toBeInTheDocument();
  });

  it("validates the candidate with the X-Staff-Token header, stores it, and mounts the dashboard", async () => {
    const fetchMock = vi.fn(async () => jsonResponse(200));
    vi.stubGlobal("fetch", fetchMock);
    const user = userEvent.setup();
    renderGate();

    await user.type(
      screen.getByPlaceholderText("Enter the staff token"),
      "s3cret-staff-token",
    );
    await user.click(screen.getByRole("button", { name: "Open dashboard" }));

    expect(await screen.findByTestId("dashboard")).toBeInTheDocument();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBe("s3cret-staff-token");

    const [url, init] = fetchMock.mock.calls[0] as unknown as [
      string,
      RequestInit,
    ];
    expect(url).toBe("/api/operations/lot-board");
    expect(new Headers(init.headers).get(STAFF_TOKEN_HEADER)).toBe(
      "s3cret-staff-token",
    );
  });

  it("rejects an invalid token at the gate (401 probe) without storing it", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse(401)));
    const user = userEvent.setup();
    renderGate();

    await user.type(
      screen.getByPlaceholderText("Enter the staff token"),
      "wrong-token",
    );
    await user.click(screen.getByRole("button", { name: "Open dashboard" }));

    expect(
      await screen.findByText("That token was rejected (401). Check it and try again."),
    ).toBeInTheDocument();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(screen.queryByTestId("dashboard")).not.toBeInTheDocument();
  });

  it("reports an unreachable API host distinctly from a wrong token", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new TypeError("fetch failed");
      }),
    );
    const user = userEvent.setup();
    renderGate();

    await user.type(
      screen.getByPlaceholderText("Enter the staff token"),
      "any-token",
    );
    await user.click(screen.getByRole("button", { name: "Open dashboard" }));

    expect(
      await screen.findByText(/Could not reach the API host/),
    ).toBeInTheDocument();
    expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it("clears the stored token and re-shows the gate when a staff request 401s mid-session", async () => {
    // Held token from a previous gate pass; the API has since rotated its secret.
    sessionStorage.setItem(STORAGE_KEY, "stale-token");
    vi.stubGlobal("fetch", vi.fn(async () => jsonResponse(401)));
    const user = userEvent.setup();

    function ProbeChild() {
      const { staffFetch } = useStaffAuth();
      return (
        <button onClick={() => void staffFetch("/api/operations/lot-board")}>
          probe
        </button>
      );
    }
    renderGate(<ProbeChild />);

    // Token held → the dashboard child renders, no gate.
    await user.click(screen.getByRole("button", { name: "probe" }));

    // 401 → token cleared, gate re-shown with the rejection reason (ADR 024 contract).
    await waitFor(() =>
      expect(sessionStorage.getItem(STORAGE_KEY)).toBeNull(),
    );
    expect(
      screen.getByPlaceholderText("Enter the staff token"),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "The API rejected the staff token (401). Enter a valid token.",
      ),
    ).toBeInTheDocument();
  });
});
