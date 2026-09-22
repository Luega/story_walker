"use client";

import { useEffect, useState, type FormEvent } from "react";
import { getUserSettings, saveUserSettings, type UserSettings } from "@/lib/api";

/** Mirrors the cap the API validates and the column enforces (UserSettingsDto.MaxFieldLength). */
const MAX = 600;

type Field = keyof UserSettings;

/**
 * Free text, one box per question, rather than named children and an art-style picker. What reads
 * these is a language model, and a parent describing their child in their own words tells it more
 * than any set of fields we could have guessed at in advance.
 */
const QUESTIONS: { field: Field; label: string; hint: string; placeholder: string }[] = [
  {
    field: "kids",
    label: "Who the stories are for",
    hint: "Names, ages, what they love, what scares them, how much they can read on their own.",
    placeholder: "Mia is 5 and can't get enough of dinosaurs. Leo is 7 and afraid of the dark.",
  },
  {
    field: "story",
    label: "What the stories should be like",
    hint: "Length, tone, the lessons you want them to carry, and anything to stay away from.",
    placeholder: "Gentle, a little funny, always a kind ending. Nothing with monsters.",
  },
  {
    field: "characters",
    label: "Characters that keep coming back",
    hint: "A recurring hero, a pet, a favourite toy — describe how they look, not just who they are.",
    placeholder: "Pip, a small grey kitten with bright green eyes and one white paw.",
  },
  {
    field: "artStyle",
    label: "The look of the illustrations",
    hint: "Watercolour, crayon, flat vector… and any colours you want, or want kept out.",
    placeholder: "Soft watercolour, warm colours, lots of white space.",
  },
];

const EMPTY: UserSettings = { kids: null, story: null, characters: null, artStyle: null };

export default function SettingsPage() {
  const [values, setValues] = useState<UserSettings>(EMPTY);
  const [loaded, setLoaded] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    let cancelled = false;

    getUserSettings()
      .then((settings) => {
        if (cancelled) return;
        setValues(settings);
        setLoaded(true);
      })
      .catch((e: unknown) => {
        // Say so rather than rendering empty boxes, which would read as "you have saved nothing"
        // and invite the user to overwrite context they cannot see.
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed to load your settings.");
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const tooLong = QUESTIONS.some(({ field }) => (values[field] ?? "").trim().length > MAX);

  function update(field: Field, value: string) {
    setValues((v) => ({ ...v, [field]: value }));
    setSaved(false);
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (saving || tooLong) return;

    setSaving(true);
    setError(null);
    setSaved(false);

    try {
      // Blank goes as null, so clearing a box actually clears the answer rather than storing "".
      const stored = await saveUserSettings({
        kids: blankToNull(values.kids),
        story: blankToNull(values.story),
        characters: blankToNull(values.characters),
        artStyle: blankToNull(values.artStyle),
      });
      setValues(stored);
      setSaved(true);
    } catch (e: unknown) {
      // What the user typed stays in the boxes: a failed save must not cost them their words.
      setError(e instanceof Error ? e.message : "Failed to save your settings.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-8">
      <div>
        <h1 className="font-display text-4xl font-semibold tracking-tight">Story settings</h1>
        <p className="mt-1 max-w-2xl text-muted">
          Tell us this once and the agent will know it every time. It treats your answers as a
          starting point and checks them with you before writing, so you can still change your mind
          for any one story.
        </p>
      </div>

      {error && (
        <p role="alert" className="rounded-lg bg-red-100 px-4 py-3 text-red-800">
          {error}
        </p>
      )}

      <div className="space-y-4">
        {QUESTIONS.map(({ field, label, hint, placeholder }) => {
          const value = values[field] ?? "";
          const over = value.trim().length > MAX;

          return (
            <div key={field} className="rounded-2xl border border-border bg-surface p-5">
              <label
                htmlFor={field}
                className="font-display text-2xl font-semibold tracking-tight"
              >
                {label}
              </label>
              <p className="mt-1 mb-3 text-sm text-muted">{hint}</p>
              <textarea
                id={field}
                value={value}
                onChange={(e) => update(field, e.target.value)}
                disabled={!loaded || saving}
                rows={4}
                placeholder={placeholder}
                className="w-full resize-y rounded-xl border border-border bg-background px-3 py-2 outline-none focus:border-accent disabled:opacity-60"
              />
              <p className={`mt-1 text-right text-xs ${over ? "text-red-700" : "text-muted"}`}>
                {value.trim().length}/{MAX}
              </p>
            </div>
          );
        })}
      </div>

      <div className="flex items-center gap-4">
        <button
          type="submit"
          disabled={!loaded || saving || tooLong}
          className="rounded-xl bg-accent px-5 py-3 font-semibold text-accent-foreground shadow-sm transition hover:brightness-105 disabled:opacity-50"
        >
          {saving ? "Saving…" : "Save"}
        </button>
        {saved && (
          <p role="status" className="text-muted">
            Saved. New stories will use this.
          </p>
        )}
      </div>
    </form>
  );
}

const blankToNull = (value: string | null) => {
  const trimmed = (value ?? "").trim();
  return trimmed.length > 0 ? trimmed : null;
};
