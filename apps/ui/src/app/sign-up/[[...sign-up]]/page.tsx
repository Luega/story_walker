import { SignUp } from "@clerk/nextjs";

export default function SignUpPage() {
  return (
    <div className="flex justify-center py-8">
      {/* Without this Clerk lands people back on /, the landing page, whose only action is
          "Get started" — the dashboard is where a signed-in user has something to do. */}
      <SignUp fallbackRedirectUrl="/dashboard" />
    </div>
  );
}
