import { SignIn } from "@clerk/nextjs";

export default function SignInPage() {
  return (
    <div className="flex justify-center py-8">
      {/* Without this Clerk lands people back on /, the landing page, whose only action is
          "Inizia" — the dashboard is where a signed-in user has something to do. */}
      <SignIn fallbackRedirectUrl="/dashboard" />
    </div>
  );
}
