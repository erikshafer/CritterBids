import { afterEach, describe, expect, it, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { SessionProvider, useSession } from "@/session/SessionContext";

function SessionDisplay() {
  const {
    participantId,
    status,
    isRegisteredSeller,
    registerAsSeller,
    isRegistering,
    registrationError,
  } = useSession();

  return (
    <div>
      <p data-testid="status">{status}</p>
      <p data-testid="participantId">{participantId ?? "none"}</p>
      <p data-testid="isSeller">{String(isRegisteredSeller)}</p>
      <p data-testid="isRegistering">{String(isRegistering)}</p>
      {registrationError && (
        <p data-testid="regError">{registrationError}</p>
      )}
      <button onClick={() => void registerAsSeller()}>Register</button>
    </div>
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("SessionContext", () => {
  it("establishes a session and provides the participant ID", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => ({
        ok: true,
        status: 200,
        headers: new Headers({
          Location: "/api/participants/test-participant",
        }),
        json: async () => ({ value: "test-participant" }),
      })),
    );

    render(
      <SessionProvider>
        <SessionDisplay />
      </SessionProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("established"),
    );
    expect(screen.getByTestId("participantId")).toHaveTextContent(
      "test-participant",
    );
    expect(screen.getByTestId("isSeller")).toHaveTextContent("false");
  });

  it("registers as seller on 200 and persists to sessionStorage", async () => {
    const user = userEvent.setup();

    sessionStorage.setItem(
      "critterbids.seller.participantId",
      "test-participant",
    );

    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      void init;
      if (url.includes("register-seller")) {
        return {
          ok: true,
          status: 200,
          headers: new Headers(),
          json: async () => null,
        };
      }
      return {
        ok: true,
        status: 200,
        headers: new Headers({
          Location: "/api/participants/test-participant",
        }),
        json: async () => ({ value: "test-participant" }),
      };
    });
    vi.stubGlobal("fetch", fetchMock);

    render(
      <SessionProvider>
        <SessionDisplay />
      </SessionProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("established"),
    );

    await user.click(screen.getByText("Register"));

    await waitFor(() =>
      expect(screen.getByTestId("isSeller")).toHaveTextContent("true"),
    );
    expect(sessionStorage.getItem("critterbids.seller.isRegisteredSeller")).toBe(
      "true",
    );

    const regCall = fetchMock.mock.calls.find(
      (args) => String(args[0]).includes("register-seller"),
    );
    expect(regCall).toBeDefined();
    const body = JSON.parse(regCall![1]!.body as string);
    expect(body.participantId).toBe("test-participant");
  });

  it("treats 409 (already registered) as success", async () => {
    const user = userEvent.setup();

    sessionStorage.setItem(
      "critterbids.seller.participantId",
      "test-participant",
    );

    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (url.includes("register-seller")) {
          return {
            ok: false,
            status: 409,
            headers: new Headers(),
            json: async () => null,
          };
        }
        return {
          ok: true,
          status: 200,
          headers: new Headers({
            Location: "/api/participants/test-participant",
          }),
          json: async () => ({ value: "test-participant" }),
        };
      }),
    );

    render(
      <SessionProvider>
        <SessionDisplay />
      </SessionProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("established"),
    );

    await user.click(screen.getByText("Register"));

    await waitFor(() =>
      expect(screen.getByTestId("isSeller")).toHaveTextContent("true"),
    );
  });

  it("surfaces registration errors on non-200/409 responses", async () => {
    const user = userEvent.setup();

    sessionStorage.setItem(
      "critterbids.seller.participantId",
      "test-participant",
    );

    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (url.includes("register-seller")) {
          return {
            ok: false,
            status: 400,
            headers: new Headers(),
            json: async () => null,
          };
        }
        return {
          ok: true,
          status: 200,
          headers: new Headers({
            Location: "/api/participants/test-participant",
          }),
          json: async () => ({ value: "test-participant" }),
        };
      }),
    );

    render(
      <SessionProvider>
        <SessionDisplay />
      </SessionProvider>,
    );

    await waitFor(() =>
      expect(screen.getByTestId("status")).toHaveTextContent("established"),
    );

    await user.click(screen.getByText("Register"));

    await waitFor(() =>
      expect(screen.getByTestId("regError")).toBeInTheDocument(),
    );
    expect(screen.getByTestId("isSeller")).toHaveTextContent("false");
  });

  it("restores seller registration state from sessionStorage", () => {
    sessionStorage.setItem(
      "critterbids.seller.participantId",
      "restored-participant",
    );
    sessionStorage.setItem("critterbids.seller.isRegisteredSeller", "true");

    vi.stubGlobal("fetch", vi.fn());

    render(
      <SessionProvider>
        <SessionDisplay />
      </SessionProvider>,
    );

    expect(screen.getByTestId("isSeller")).toHaveTextContent("true");
    expect(screen.getByTestId("status")).toHaveTextContent("established");
  });
});
