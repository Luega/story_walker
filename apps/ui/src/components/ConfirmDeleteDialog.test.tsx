import { render, screen } from "@testing-library/react";
import { userEvent } from "vitest/browser";
import { describe, expect, it, vi } from "vitest";
import { ConfirmDeleteDialog } from "./ConfirmDeleteDialog";

// These assertions are the reason the suite runs in a real browser: showModal(), the top-layer
// :modal state and Escape-to-cancel are implemented by the browser, and jsdom has none of them.

function renderDialog({ busy = false } = {}) {
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  render(
    <ConfirmDeleteDialog
      title="The Kind Dragon"
      busy={busy}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />,
  );
  const dialog = document.querySelector("dialog")!;
  return { dialog, onConfirm, onCancel };
}

const confirmButton = () => screen.getByRole("button", { name: "Delete" });
const cancelButton = () => screen.getByRole("button", { name: "Cancel" });

describe("presentation", () => {
  it("opens as a modal dialog", () => {
    const { dialog } = renderDialog();

    expect(dialog.open).toBe(true);
    // :modal is only true for a dialog opened with showModal() — it proves the page behind is inert.
    expect(dialog.matches(":modal")).toBe(true);
  });

  it("names the story being deleted and warns that it is irreversible", () => {
    renderDialog();

    expect(screen.getByRole("heading", { name: "Delete this story?" })).toBeInTheDocument();
    expect(screen.getByText(/The Kind Dragon/)).toBeInTheDocument();
    expect(screen.getByText(/cannot be undone/)).toBeInTheDocument();
  });

  it("puts the initial focus on Cancel, so a stray Enter does not delete", () => {
    renderDialog();

    expect(cancelButton()).toHaveFocus();
    expect(confirmButton()).not.toHaveFocus();
  });
});

describe("choosing", () => {
  it("confirms when Delete is pressed", async () => {
    const { onConfirm, onCancel } = renderDialog();

    await userEvent.click(confirmButton());

    expect(onConfirm).toHaveBeenCalledOnce();
    expect(onCancel).not.toHaveBeenCalled();
  });

  it("cancels when Cancel is pressed", async () => {
    const { onConfirm, onCancel } = renderDialog();

    await userEvent.click(cancelButton());

    expect(onCancel).toHaveBeenCalledOnce();
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it("cancels on Escape", async () => {
    const { onConfirm, onCancel } = renderDialog();

    await userEvent.keyboard("{Escape}");

    expect(onCancel).toHaveBeenCalledOnce();
    expect(onConfirm).not.toHaveBeenCalled();
  });
});

describe("while the delete is in flight", () => {
  it("disables both buttons so the request cannot be sent twice or abandoned", () => {
    renderDialog({ busy: true });

    // The confirm button relabels itself while working, so it is no longer "Delete".
    expect(screen.getByRole("button", { name: "Deleting…" })).toBeDisabled();
    expect(cancelButton()).toBeDisabled();
    expect(screen.queryByRole("button", { name: "Delete" })).not.toBeInTheDocument();
  });

  it("ignores Escape, so the dialog cannot be dismissed mid-request", async () => {
    const { dialog, onCancel } = renderDialog({ busy: true });

    await userEvent.keyboard("{Escape}");

    expect(onCancel).not.toHaveBeenCalled();
    expect(dialog.open).toBe(true);
  });
});
