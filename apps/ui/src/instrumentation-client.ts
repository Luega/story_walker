// Browser telemetry to Application Insights (ADR-0023). Next runs this file once, before the app
// becomes interactive. It records page views (the first load and every App Router navigation),
// unhandled errors, and every fetch to /api/* as a dependency. That fetch also carries a W3C
// traceparent, which the Next server continues, so a click and the API work it causes are one
// operation.
//
// Opt-in: NEXT_PUBLIC_ variables are inlined at build time, so a build without the connection
// string ships none of this.
//
// The connection string ends up in the client bundle, which is the design of browser monitoring.
// It is an ingestion key: anyone holding it can send telemetry, but nobody can read any.

import { ApplicationInsights, DistributedTracingModes } from "@microsoft/applicationinsights-web";

let appInsights: ApplicationInsights | undefined;

const connectionString = process.env.NEXT_PUBLIC_APPLICATIONINSIGHTS_CONNECTION_STRING?.trim();

if (connectionString) {
  // Monitoring must never be the reason the app fails to start.
  try {
    appInsights = new ApplicationInsights({
      config: {
        connectionString,
        distributedTracingMode: DistributedTracingModes.W3C,
        // Headers carry Clerk's session and our own content types; neither belongs in telemetry.
        enableRequestHeaderTracking: false,
        enableResponseHeaderTracking: false,
        // No ai_user / ai_session cookies: they are analytics cookies, and the app has no consent
        // banner to ask for them. The cost is that Users and Sessions counts are unavailable.
        disableCookiesUsage: true,
      },
    });
    appInsights.loadAppInsights();

    appInsights.addTelemetryInitializer((item) => {
      // Same-origin dependencies only. Everything else the page calls is Clerk, whose URLs carry
      // a session token in the query string (__clerk_db_jwt) that must never reach telemetry.
      // At this point target is the request's full URL; it is cut down to a host only later.
      if (item.baseType === "RemoteDependencyData") {
        const target = String(item.baseData?.target ?? "");
        if (new URL(target, location.href).origin !== location.origin) return false;
      }

      // The role name on the Application Map, next to storywalker-ui (the Next server).
      item.tags = item.tags ?? {};
      item.tags["ai.cloud.role"] = "storywalker-browser";
    });

    // The first load; onRouterTransitionStart below covers every navigation after it.
    trackPageView(location.href);
  } catch (error) {
    console.warn("Application Insights failed to start.", error);
  }
}

// Next calls this at the start of every App Router navigation, back and forward included. The SDK's
// own enableAutoRouteTracking is not used: it also fires on the history.replaceState calls made while
// the page loads, so it counted every first load twice.
export function onRouterTransitionStart(url: string) {
  trackPageView(new URL(url, location.href).href);
}

// Named by path rather than document.title: at the start of a transition the title is still the
// previous page's, and every page shares one anyway.
function trackPageView(uri: string) {
  appInsights?.trackPageView({ name: new URL(uri).pathname, uri });
}
