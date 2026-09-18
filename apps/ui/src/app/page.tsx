import Link from "next/link";

// Public entry point. A server component with no data fetching: nothing here depends on who is
// looking, so it renders the same for a visitor and for a signed-in user (the header's UserButton
// is what tells them apart).
const STEPS = [
  {
    title: "Racconta l'idea",
    body: "Chiacchieri con l'agente: chi è il protagonista, dove si svolge, per quale bambino è.",
  },
  {
    title: "L'agente scrive il piano",
    body: "Trasforma la conversazione in un brief completo — titolo, età, stile illustrativo, sei pagine.",
  },
  {
    title: "Ricevi la storia",
    body: "Testo e illustrazioni vengono generati insieme, con personaggi coerenti da una pagina all'altra.",
  },
];

export default function LandingPage() {
  return (
    <div className="space-y-16 py-8">
      <section className="max-w-2xl space-y-6">
        <h1 className="font-display text-5xl font-semibold tracking-tight sm:text-6xl">
          Storie illustrate, scritte per il tuo bambino.
        </h1>
        <p className="text-lg text-muted">
          Descrivi l&apos;idea a voce tua. Story Walker la trasforma in una storia illustrata di sei
          pagine, con gli stessi personaggi dall&apos;inizio alla fine.
        </p>
        <Link
          href="/sign-up"
          className="inline-block rounded-xl bg-accent px-6 py-3 font-semibold text-accent-foreground shadow-sm transition hover:brightness-105"
        >
          Inizia
        </Link>
      </section>

      <section className="space-y-6">
        <h2 className="font-display text-2xl font-semibold tracking-tight">Come funziona</h2>
        <ol className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          {STEPS.map((step, i) => (
            <li
              key={step.title}
              className="rounded-2xl border border-border bg-surface px-6 py-6 space-y-2"
            >
              <span className="font-display text-3xl font-semibold text-accent">{i + 1}</span>
              <h3 className="font-semibold">{step.title}</h3>
              <p className="text-sm text-muted">{step.body}</p>
            </li>
          ))}
        </ol>
      </section>
    </div>
  );
}
