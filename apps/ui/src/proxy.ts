import { clerkMiddleware, createRouteMatcher } from "@clerk/nextjs/server";

// Next 16 renamed middleware.ts to proxy.ts; clerkMiddleware() is unchanged, only the filename is.
const isPublicRoute = createRouteMatcher(["/", "/sign-in(.*)", "/sign-up(.*)"]);

export default clerkMiddleware(async (auth, request) => {
  if (!isPublicRoute(request)) await auth.protect();
});

export const config = {
  matcher: [
    // Everything except Next's internals and static assets...
    "/((?!_next|[^?]*\\.(?:html?|css|js(?!on)|jpe?g|webp|png|gif|svg|ttf|woff2?|ico|csv|docx?|xlsx?|zip|webmanifest)).*)",
    // ...plus the API proxy route, which needs the session to mint a token...
    "/(api|trpc)(.*)",
    // ...and Clerk's own handshake path, which never reaches the session otherwise.
    "/__clerk/(.*)",
  ],
};
