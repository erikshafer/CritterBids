import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

// S5 placeholder for the six M8-S6 dashboard views: names the surface, the staff endpoint(s) it
// will query, and the read model it renders — so the shell's information architecture is real
// even while the data surfaces are not.
export interface PlaceholderViewProps {
  title: string;
  description: string;
  endpoint: string;
}

export function PlaceholderView({
  title,
  description,
  endpoint,
}: PlaceholderViewProps) {
  return (
    <div className="space-y-6">
      <h1 className="text-3xl font-semibold tracking-tight">{title}</h1>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Arrives in M8-S6</CardTitle>
          <CardDescription className="text-base">
            {description}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-muted-foreground text-sm">
            Data source: <code className="font-mono">{endpoint}</code>
          </p>
        </CardContent>
      </Card>
    </div>
  );
}
