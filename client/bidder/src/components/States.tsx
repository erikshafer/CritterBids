// Small shared presentational states for the read surfaces (loading skeletons live per-page).

export function EmptyState({ message }: { message: string }) {
  return (
    <div className="border-border/60 rounded-xl border border-dashed py-16 text-center">
      <p className="text-muted-foreground text-sm">{message}</p>
    </div>
  );
}

export function ErrorState({
  message,
  onRetry,
}: {
  message: string;
  onRetry?: () => void;
}) {
  return (
    <div className="bg-destructive/10 text-destructive rounded-xl px-4 py-10 text-center">
      <p className="text-sm font-medium">Something went wrong.</p>
      <p className="text-destructive/80 mt-1 text-xs">{message}</p>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="border-destructive/40 hover:bg-destructive/10 mt-4 rounded-md border px-3 py-1.5 text-xs font-medium"
        >
          Try again
        </button>
      )}
    </div>
  );
}
