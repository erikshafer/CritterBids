import { HubConnectionState, type HubConnection } from "@microsoft/signalr";
import type { Mock } from "vitest";

export class FakeHubConnection {
  state: HubConnectionState = HubConnectionState.Disconnected;
  private handlers = new Map<string, (payload: unknown) => void>();
  private reconnectedCallbacks: Array<(connectionId?: string) => void> = [];
  invoke: Mock;

  constructor(invoke?: Mock) {
    this.invoke = invoke ?? ((() => Promise.resolve()) as unknown as Mock);
  }

  on(method: string, handler: (payload: unknown) => void) {
    this.handlers.set(method, handler);
  }

  onreconnecting() {}

  onreconnected(callback: (connectionId?: string) => void) {
    this.reconnectedCallbacks.push(callback);
  }

  onclose() {}

  async start() {
    this.state = HubConnectionState.Connected;
  }

  async stop() {
    this.state = HubConnectionState.Disconnected;
  }

  emit(payload: unknown) {
    const handler = this.handlers.get("ReceiveMessage");
    handler?.(payload);
  }

  simulateReconnect() {
    this.state = HubConnectionState.Connected;
    for (const callback of this.reconnectedCallbacks) callback();
  }

  asHubConnection(): HubConnection {
    return this as unknown as HubConnection;
  }
}
