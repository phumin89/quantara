import type { HubConnection } from "@microsoft/signalr";

type RealtimeHub = Pick<
  HubConnection,
  "state" | "onclose" | "onreconnecting" | "onreconnected" | "start" | "stop"
>;

export type RealtimeConnectionState = "connected" | "offline" | "reconnecting";

export function startRealtimeConnection(
  hub: RealtimeHub,
  onState: (state: RealtimeConnectionState) => void,
  retryMs = 3000,
): () => void {
  let disposed = false;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let startInFlight: Promise<void> | undefined;

  const clearRetry = () => {
    if (retryTimer !== undefined) {
      clearTimeout(retryTimer);
      retryTimer = undefined;
    }
  };

  const retry = () => {
    if (disposed || retryTimer !== undefined) return;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      void connect();
    }, retryMs);
  };

  const connect = async (): Promise<void> => {
    if (disposed || startInFlight || hub.state !== "Disconnected") return;
    startInFlight = hub.start();
    try {
      await startInFlight;
      if (!disposed) {
        clearRetry();
        onState("connected");
      }
    } catch {
      if (!disposed) {
        onState("offline");
        retry();
      }
    } finally {
      startInFlight = undefined;
    }
  };

  hub.onreconnecting(() => {
    if (!disposed) onState("reconnecting");
  });
  hub.onreconnected(() => {
    if (!disposed) onState("connected");
  });
  hub.onclose(() => {
    if (!disposed) {
      onState("offline");
      retry();
    }
  });

  void connect();

  return () => {
    if (disposed) return;
    disposed = true;
    clearRetry();
    const stop = () => { void hub.stop().catch(() => undefined); };
    if (startInFlight) void startInFlight.then(stop, stop);
    else stop();
  };
}
