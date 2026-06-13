import { useNavigate } from "@tanstack/react-router";

import { useSession } from "@/session/SessionContext";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

export function HomePage() {
  const {
    participantId,
    status,
    isRegisteredSeller,
    registerAsSeller,
    isRegistering,
    registrationError,
  } = useSession();
  const navigate = useNavigate();

  if (isRegisteredSeller) {
    void navigate({ to: "/listings" });
    return null;
  }

  return (
    <div className="mx-auto max-w-lg space-y-6 py-8">
      <div className="space-y-2 text-center">
        <h2 className="text-2xl font-semibold tracking-tight text-foreground">
          Welcome to the Seller Console
        </h2>
        <p className="text-muted-foreground text-sm">
          Register as a seller to list items on CritterBids.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Seller Registration</CardTitle>
          <CardDescription>
            One-click registration using your active session. Once registered,
            you can create and manage listings.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {status === "establishing" && (
            <p className="text-muted-foreground text-sm" role="status">
              Starting your session...
            </p>
          )}

          {status === "error" && (
            <p className="text-destructive text-sm" role="alert">
              Session could not be established. Please refresh the page.
            </p>
          )}

          {status === "established" && (
            <>
              <dl className="rounded-lg border border-border bg-muted/30 p-3 text-sm">
                <div className="flex justify-between py-1">
                  <dt className="text-muted-foreground">Participant ID</dt>
                  <dd className="font-mono text-xs text-foreground">
                    {participantId}
                  </dd>
                </div>
              </dl>

              <Button
                onClick={() => void registerAsSeller().then(() => {
                  void navigate({ to: "/listings" });
                })}
                disabled={isRegistering}
                className="w-full"
              >
                {isRegistering ? "Registering..." : "Register as Seller"}
              </Button>

              {registrationError && (
                <p className="text-destructive text-xs" role="alert">
                  {registrationError}
                </p>
              )}
            </>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
