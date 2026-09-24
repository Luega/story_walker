// Loaded by instrumentation.ts on the Node runtime only, and only when a connection string is set.
// Next already emits a span per request and per render; this gives those spans an exporter, plus
// the SDK's own HTTP instrumentation, exceptions and metrics.

// Renamed on import: it is not a React hook, but the name trips react-hooks/rules-of-hooks.
import { useAzureMonitor as startAzureMonitor } from "@azure/monitor-opentelemetry";
import { resourceFromAttributes } from "@opentelemetry/resources";

// The Application Insights role name: what this process is called on the Application Map, next to
// the API and the worker.
const resource = resourceFromAttributes({ "service.name": "storywalker-ui" });

startAzureMonitor({
  azureMonitorExporterOptions: {
    connectionString: process.env.APPLICATIONINSIGHTS_CONNECTION_STRING,
  },
  resource,
});
