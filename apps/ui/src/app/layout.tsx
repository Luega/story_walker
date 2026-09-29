import type { Metadata } from "next";
import Link from "next/link";
import { ClerkProvider, Show, UserButton } from "@clerk/nextjs";
import { Backdrop } from "@/components/Backdrop";
import { CookieConsent } from "@/components/CookieConsent";
import { Fredoka, Nunito } from "next/font/google";
import "./globals.css";

// Two rounded faces with the same soft terminals (ADR-0033): Fredoka for titles and the story
// itself, Nunito for the rest of the interface.
const fredoka = Fredoka({
  variable: "--font-fredoka",
  subsets: ["latin"],
});

const nunito = Nunito({
  variable: "--font-nunito",
  subsets: ["latin"],
});

const WORDMARK = "font-display text-xl font-semibold tracking-tight text-accent sm:text-2xl";
const NAV_LINK =
  "rounded-full px-3 py-2 text-sm font-bold transition hover:bg-lilac-soft hover:text-accent";

// Clerk's sign-in, sign-up and user menu in the site's own colours, type and corners. The values
// are the CSS variables themselves, so the night palette carries over without a second theme.
const CLERK_APPEARANCE = {
  variables: {
    colorPrimary: "var(--accent)",
    colorPrimaryForeground: "var(--accent-foreground)",
    colorDanger: "var(--danger)",
    colorForeground: "var(--foreground)",
    colorMutedForeground: "var(--muted)",
    colorBackground: "var(--surface)",
    colorInput: "var(--background)",
    colorInputForeground: "var(--foreground)",
    colorBorder: "var(--border)",
    fontFamily: "var(--font-nunito)",
    borderRadius: "1rem",
  },
};

export const metadata: Metadata = {
  title: "Story Walker",
  description: "Illustrated stories for children, created together with an agent.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${fredoka.variable} ${nunito.variable} h-full antialiased`}
    >
      <body className="isolate flex min-h-full flex-col font-sans">
        <Backdrop />
        {/* Clerk Core 3 wants the provider inside <body>, not wrapping <html>. */}
        <ClerkProvider appearance={CLERK_APPEARANCE}>
          <header className="px-4 pt-4 sm:px-6">
            {/* A floating pill rather than a ruled bar: the site's first rounded shape. */}
            <div className="mx-auto flex max-w-6xl items-center justify-between gap-3 rounded-full border-2 border-border bg-surface px-4 shadow-soft py-2.5 sm:px-6">
              {/* The wordmark leads home: the landing for visitors, the dashboard once signed in. */}
              <Show when="signed-out">
                <Link href="/" className={WORDMARK}>
                  Story Walker
                </Link>
              </Show>
              <Show when="signed-in">
                <Link href="/dashboard" className={WORDMARK}>
                  Story Walker
                </Link>
              </Show>
              <div className="flex items-center gap-1 sm:gap-2">
                {/* <Show> replaced <SignedIn>/<SignedOut> in Clerk Core 3; the old ones throw. */}
                <Show when="signed-out">
                  <Link href="/sign-in" className={NAV_LINK}>
                    Sign in
                  </Link>
                  <Link href="/sign-up" className="btn-primary px-4 py-2 text-sm">
                    Sign up
                  </Link>
                </Show>
                <Show when="signed-in">
                  <Link href="/dashboard" className={NAV_LINK}>
                    Your stories
                  </Link>
                  <Link href="/settings" className={NAV_LINK}>
                    Settings
                  </Link>
                  <UserButton />
                </Show>
              </div>
            </div>
          </header>
          <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6">{children}</main>
          <footer className="mt-8 text-lilac-soft">
            {/* A wavy edge in the footer's own colour, so the page ends like a painted hill. */}
            <svg
              aria-hidden
              viewBox="0 0 1440 48"
              preserveAspectRatio="none"
              className="block h-8 w-full fill-current sm:h-12"
            >
              <path d="M0 24C120 4 240 4 360 24s240 20 360 0 240-20 360 0 240 20 360 0v24H0Z" />
            </svg>
            <div className="bg-lilac-soft">
              <div className="mx-auto flex max-w-6xl gap-6 px-4 pt-2 pb-6 text-sm font-semibold text-muted sm:px-6">
                <Link href="/privacy" className="hover:text-accent">
                  Privacy
                </Link>
                {/* Preferences link and consent banner; renders nothing without browser telemetry. */}
                <CookieConsent />
              </div>
            </div>
          </footer>
        </ClerkProvider>
      </body>
    </html>
  );
}
