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
import { COOKIE_CONSENT_EVENT, readCookieConsent, type CookieConsent } from "@/lib/cookieConsent";

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
        // ai_user / ai_session are analytics cookies: off until the visitor accepts them in the
        // banner (components/CookieConsent.tsx). Without them telemetry still flows, but Users and
        // Sessions cannot be counted.
        cookieCfg: { enabled: readCookieConsent() === "accepted" },
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

    // An answer given (or changed) while the page is open applies at once.
    window.addEventListener(COOKIE_CONSENT_EVENT, (event) => {
      applyCookieConsent((event as CustomEvent<CookieConsent>).detail);
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

function applyCookieConsent(consent: CookieConsent) {
  const cookies = appInsights?.getCookieMgr();
  if (!cookies) return;

  cookies.setEnabled(consent === "accepted");
  if (consent === "rejected") {
    // Withdrawing consent has to remove what was set under it, not only stop setting more. purge
    // works even with the manager disabled, which it now is.
    cookies.purge("ai_user");
    cookies.purge("ai_session");
  }
}
