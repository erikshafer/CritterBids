import { useState, type FormEvent, type ReactNode } from "react";

import { useStaffAuth } from "@/auth/StaffAuthContext";
import { validateStaffToken } from "@/auth/staffApi";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";

// The staff auth gate (M8-S5, ADR 024). Not a login flow — a single text input for the shared
// staff secret. The candidate token is validated against a real staff-gated endpoint BEFORE it is
// stored, so a typo is caught at the gate (with the API's actual 401) rather than as an opaque
// WebSocket failure on the hub connect — browsers hide the upgrade status code, so the HTTP probe
// is the only reliable 401 detector. Children (the dashboard + the OperationsHub connection)
// mount only once a token is held.
export function AuthGate({ children }: { children: ReactNode }) {
  const { token } = useStaffAuth();
  if (token === null) return <GateScreen />;
  return children;
}

type GateStatus = "idle" | "validating" | "invalid" | "unreachable";

function GateScreen() {
  const { setToken, authError } = useStaffAuth();
  const [candidate, setCandidate] = useState("");
  const [status, setStatus] = useState<GateStatus>("idle");

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = candidate.trim();
    if (trimmed === "" || status === "validating") return;

    setStatus("validating");
    const result = await validateStaffToken(trimmed);
    if (result === "valid") {
      setToken(trimmed); // unmounts the gate; status state dies with it
      return;
    }
    setStatus(result);
  }

  return (
    <div className="bg-background text-foreground flex min-h-screen items-center justify-center px-4">
      <Card className="w-full max-w-md">
        <CardHeader>
          <CardTitle className="text-2xl tracking-tight">
            CritterBids Operations
          </CardTitle>
          <CardDescription className="text-base">
            Staff access only. Enter the staff token to open the dashboard.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={(event) => void onSubmit(event)} className="space-y-4">
            <label className="block space-y-2">
              <span className="text-sm font-medium">Staff token</span>
              <Input
                type="password"
                name="staff-token"
                autoComplete="off"
                autoFocus
                value={candidate}
                onChange={(event) => setCandidate(event.target.value)}
                placeholder="Enter the staff token"
              />
            </label>
            <Button
              type="submit"
              className="w-full"
              disabled={candidate.trim() === "" || status === "validating"}
            >
              {status === "validating" ? "Checking…" : "Open dashboard"}
            </Button>
            {status === "invalid" && (
              <p role="alert" className="text-destructive text-sm">
                That token was rejected (401). Check it and try again.
              </p>
            )}
            {status === "unreachable" && (
              <p role="alert" className="text-destructive text-sm">
                Could not reach the API host. Is it running on the dev-proxy
                target?
              </p>
            )}
            {status === "idle" && authError !== null && (
              <p role="alert" className="text-destructive text-sm">
                {authError}
              </p>
            )}
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
