import { useEffect, useRef } from "react";
import type { FormEvent, MouseEvent } from "react";
import "./waiter-cancel-dialog.css";

type WaiterCancelDialogProps = {
  orderId: number;
  tableNumber: number | undefined;
  seatNumber: number | null;
  itemCount: number;
  reason: string;
  confirmed: boolean;
  validationError: string | null;
  cancelling: boolean;
  canCancel: boolean;
  restrictionMessage: string | null;
  onReasonChange: (value: string) => void;
  onConfirmedChange: (value: boolean) => void;
  onClose: () => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
};

function WaiterCancelDialog({
  orderId,
  tableNumber,
  seatNumber,
  itemCount,
  reason,
  confirmed,
  validationError,
  cancelling,
  canCancel,
  restrictionMessage,
  onReasonChange,
  onConfirmedChange,
  onClose,
  onSubmit,
}: WaiterCancelDialogProps) {
  const reasonRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    reasonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !cancelling) onClose();
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [cancelling, onClose]);

  const closeFromBackdrop = (event: MouseEvent<HTMLDivElement>) => {
    if (event.target === event.currentTarget && !cancelling) onClose();
  };

  return (
    <div className="waiter-cancel-overlay" onMouseDown={closeFromBackdrop}>
      <section
        className="waiter-cancel-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="waiter-cancel-title"
        aria-describedby="waiter-cancel-description"
      >
        <header className="waiter-cancel-card-header">
          <span className="waiter-cancel-icon" aria-hidden="true">!</span>
          <div className="waiter-cancel-heading">
            <span className="waiter-cancel-kicker">ORDER ACTION</span>
            <h2 id="waiter-cancel-title">Cancel order</h2>
            <p id="waiter-cancel-description">
              {canCancel
                ? "This will close the unpaid order. You cannot undo this action from the waiter screen."
                : "This order cannot be cancelled from the waiter screen."}
            </p>
          </div>
          <button
            type="button"
            className="waiter-cancel-close"
            aria-label="Close cancellation dialog"
            disabled={cancelling}
            onClick={onClose}
          >
            ×
          </button>
        </header>

        <div className="waiter-cancel-summary" aria-label="Order summary">
          <span><small>ORDER</small><strong>#{orderId}</strong></span>
          <span><small>TABLE</small><strong>{tableNumber ?? "—"}{seatNumber ? ` · Seat ${seatNumber}` : ""}</strong></span>
          <span><small>ITEMS</small><strong>{itemCount}</strong></span>
        </div>

        {canCancel ? (
          <form className="waiter-cancel-form" onSubmit={onSubmit}>
            <label className="waiter-cancel-field" htmlFor="waiter-cancel-reason">
              <span>Reason for cancellation <em>Required</em></span>
              <textarea
                id="waiter-cancel-reason"
                ref={reasonRef}
                rows={4}
                maxLength={500}
                value={reason}
                aria-invalid={Boolean(validationError && !reason.trim())}
                aria-describedby="waiter-cancel-reason-help"
                disabled={cancelling}
                placeholder="Briefly explain why this order is being cancelled"
                onChange={(event) => onReasonChange(event.target.value)}
              />
              <small id="waiter-cancel-reason-help" className="waiter-cancel-hint">
                Enter 1–500 characters. {reason.length}/500
              </small>
            </label>

            <label className="waiter-cancel-confirm">
              <input
                type="checkbox"
                checked={confirmed}
                disabled={cancelling}
                onChange={(event) => onConfirmedChange(event.target.checked)}
              />
              <span>
                I understand this closes order <strong>#{orderId}</strong> and confirm the cancellation.
              </span>
            </label>

            {validationError && (
              <p className="waiter-cancel-error" role="alert">{validationError}</p>
            )}

            <footer className="waiter-cancel-actions">
              <button
                type="button"
                className="waiter-cancel-keep"
                disabled={cancelling}
                onClick={onClose}
              >
                Keep order
              </button>
              <button
                type="submit"
                className="waiter-cancel-submit"
                disabled={cancelling}
              >
                {cancelling ? "Cancelling…" : "Confirm cancellation"}
              </button>
            </footer>
          </form>
        ) : (
          <div className="waiter-cancel-restriction">
            <div className="waiter-cancel-restriction-icon" aria-hidden="true">i</div>
            <div>
              <strong>Cancellation unavailable</strong>
              <p>{restrictionMessage ?? "This order is no longer active."}</p>
            </div>
            <footer className="waiter-cancel-actions">
              <button type="button" className="waiter-cancel-keep" onClick={onClose}>
                Close
              </button>
            </footer>
          </div>
        )}
      </section>
    </div>
  );
}

export default WaiterCancelDialog;
