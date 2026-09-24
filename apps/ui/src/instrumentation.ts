// Server-side telemetry to Application Insights (ADR-0023). Next calls register() once per server
// instance, before the first request. The Azure Monitor SDK is Node-only, so it is imported only on
// the Node runtime, from its own file, as the Next OpenTelemetry guide recommends.
//
// Opt-in: with no connection string nothing is loaded, so `next dev` and the tests are unaffected.

export async function register() {
  if (process.env.NEXT_RUNTIME !== "nodejs") return;
  if (!process.env.APPLICATIONINSIGHTS_CONNECTION_STRING?.trim()) return;

  await import("./instrumentation.node");
}
