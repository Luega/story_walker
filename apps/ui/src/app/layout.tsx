import type { Metadata } from "next";
import Link from "next/link";
import { ClerkProvider, Show, UserButton } from "@clerk/nextjs";
import { Fraunces, Nunito } from "next/font/google";
import "./globals.css";

const fraunces = Fraunces({
  variable: "--font-fraunces",
  subsets: ["latin"],
});

const nunito = Nunito({
  variable: "--font-nunito",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Story Walker",
  description: "Illustrated stories for children, created together with an agent.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="en"
      className={`${fraunces.variable} ${nunito.variable} h-full antialiased`}
    >
      <body className="flex min-h-full flex-col font-sans">
        {/* Clerk Core 3 wants the provider inside <body>, not wrapping <html>. */}
        <ClerkProvider>
          <header className="border-b border-border">
            <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-4 sm:px-6">
              {/* The wordmark leads home: the landing for visitors, the dashboard once signed in. */}
              <Show when="signed-out">
                <Link href="/" className="font-display text-2xl font-semibold tracking-tight">
                  Story Walker
                </Link>
              </Show>
              <Show when="signed-in">
                <Link
                  href="/dashboard"
                  className="font-display text-2xl font-semibold tracking-tight"
                >
                  Story Walker
                </Link>
              </Show>
              <div className="flex items-center gap-4">
                {/* <Show> replaced <SignedIn>/<SignedOut> in Clerk Core 3; the old ones throw. */}
                <Show when="signed-out">
                  <Link href="/sign-in" className="text-sm font-semibold hover:text-accent">
                    Sign in
                  </Link>
                  <Link
                    href="/sign-up"
                    className="rounded-xl bg-accent px-4 py-2 text-sm font-semibold text-accent-foreground transition hover:brightness-105"
                  >
                    Sign up
                  </Link>
                </Show>
                <Show when="signed-in">
                  <Link href="/dashboard" className="text-sm font-semibold hover:text-accent">
                    Your stories
                  </Link>
                  <UserButton />
                </Show>
              </div>
            </div>
          </header>
          <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6">{children}</main>
        </ClerkProvider>
      </body>
    </html>
  );
}
