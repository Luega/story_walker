"use client";

import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import { sendAgentMessage } from "@/lib/api";

interface Message {
  role: "user" | "assistant";
  text: string;
}

const GREETING: Message = {
  role: "assistant",
  text: "Hi! Tell me about the story you'd like: who the main characters are, where it takes place, and what age it's for.",
};

/**
 * Chat with the story agent. Mounted only while open, so closing it discards the conversation.
 *
 * It does not navigate: once the agent has the brief it reports the queued story to the dashboard
 * behind it and closes, leaving the user where they started.
 */
export function NewStoryModal({
  onClose,
  onQueued,
}: {
  onClose: () => void;
  onQueued: (storyId: string) => void;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const bottomRef = useRef<HTMLDivElement>(null);

  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<Message[]>([GREETING]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    dialogRef.current?.showModal();
  }, []);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, sending]);

  async function send(e?: FormEvent) {
    e?.preventDefault();
    const text = input.trim();
    if (!text || sending) return;

    setMessages((m) => [...m, { role: "user", text }]);
    setInput("");
    setError(null);
    setSending(true);

    const turn = await sendAgentMessage(conversationId, text);
    setSending(false);

    switch (turn.kind) {
      case "reply":
        setConversationId(turn.conversationId);
        setMessages((m) => [...m, { role: "assistant", text: turn.reply }]);
        break;
      case "queued":
        onQueued(turn.storyId);
        dialogRef.current?.close();
        break;
      case "notFound":
        setConversationId(null);
        setMessages([GREETING]);
        setError("The conversation expired — let's start over.");
        break;
      case "error":
        setError(turn.detail);
        setInput(text);
        break;
    }
  }

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      void send();
    }
  }

  return (
    <dialog
      ref={dialogRef}
      onClose={onClose}
      className="m-auto flex h-[min(40rem,90vh)] w-[min(40rem,calc(100vw-2rem))] flex-col overflow-hidden rounded-2xl border border-border bg-surface p-0 text-foreground shadow-2xl backdrop:bg-black/40 backdrop:backdrop-blur-sm"
    >
      <div className="flex items-center justify-between border-b border-border px-5 py-4">
        <h2 className="font-display text-xl font-semibold">New story</h2>
        <button
          type="button"
          onClick={() => dialogRef.current?.close()}
          className="rounded-full px-2 text-2xl leading-none text-muted hover:text-foreground"
          aria-label="Close"
        >
          ×
        </button>
      </div>

      <div className="flex-1 space-y-3 overflow-y-auto px-5 py-4">
        {messages.map((m, i) => (
          <div key={i} className={m.role === "user" ? "flex justify-end" : "flex justify-start"}>
            <p
              className={`max-w-[85%] whitespace-pre-wrap rounded-2xl px-4 py-2.5 leading-relaxed ${
                m.role === "user"
                  ? "rounded-br-sm bg-accent text-accent-foreground"
                  : "rounded-bl-sm bg-background"
              }`}
            >
              {m.text}
            </p>
          </div>
        ))}
        {sending && (
          <p className="text-sm italic text-muted" aria-live="polite">
            The agent is typing…
          </p>
        )}
        <div ref={bottomRef} />
      </div>

      {error && (
        <p role="alert" className="mx-5 mb-2 rounded-lg bg-red-100 px-3 py-2 text-sm text-red-800">
          {error}
        </p>
      )}

      <form onSubmit={send} className="flex items-end gap-2 border-t border-border px-5 py-4">
        <textarea
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={onKeyDown}
          disabled={sending}
          rows={2}
          autoFocus
          placeholder="Write a message…"
          className="flex-1 resize-none rounded-xl border border-border bg-background px-3 py-2 outline-none focus:border-accent disabled:opacity-60"
        />
        <button
          type="submit"
          disabled={sending || !input.trim()}
          className="rounded-xl bg-accent px-4 py-2.5 font-semibold text-accent-foreground disabled:opacity-50"
        >
          Send
        </button>
      </form>
    </dialog>
  );
}
