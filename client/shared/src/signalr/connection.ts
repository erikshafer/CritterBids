import {
  HttpTransportType,
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";

export function createAnonymousConnection(hubUrl: string): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl)
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}

export function createTokenConnection(
  hubUrl: string,
  getToken: () => string | null,
): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(hubUrl, {
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
      accessTokenFactory: () => getToken() ?? "",
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();
}
