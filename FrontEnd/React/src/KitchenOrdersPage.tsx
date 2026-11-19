import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { getCurrentAccount, getKitchenOrders } from "./services/api";
import type { KitchenSubmissionResponse } from "./services/api";
import WorkspaceSessionActions from "./WorkspaceSessionActions";

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Could not load kitchen orders.";
}

type TableQueue = {
  tableId: number;
  submissions: KitchenSubmissionResponse[];
  itemCount: number;
  dishCount: number;
  latestSentAt: string;
};


function groupOrdersByTable(orders: KitchenSubmissionResponse[]): TableQueue[] {
  const groups = new Map<number, KitchenSubmissionResponse[]>();

  for (const order of orders) {
    const current = groups.get(order.diningTableId) ?? [];
    current.push(order);
    groups.set(order.diningTableId, current);
  }

  return Array.from(groups.entries())
    .map(([tableId, submissions]) => {
      const sorted = [...submissions].sort(
        (a, b) => new Date(a.sentAtUtc).getTime() - new Date(b.sentAtUtc).getTime(),
      );
      return {
        tableId,
        submissions: sorted,
        itemCount: sorted.reduce((total, submission) => total + submission.items.reduce((sum, item) => sum + item.quantity, 0), 0),
        dishCount: sorted.reduce((total, submission) => total + submission.items.length, 0),
        latestSentAt: sorted[sorted.length - 1]?.sentAtUtc ?? "",
      };
    })
    .sort((a, b) => new Date(a.latestSentAt).getTime() - new Date(b.latestSentAt).getTime());
}

function KitchenOrdersPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [orders, setOrders] = useState<KitchenSubmissionResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const requestVersion = useRef(0);
  const tableQueues = useMemo(() => groupOrdersByTable(orders), [orders]);

  const refresh = useCallback(async (showLoading = false) => {
    const version = ++requestVersion.current;
    if (showLoading) setLoading(true);
    setError(null);
    try {
      const currentOrders = await getKitchenOrders();
      if (version === requestVersion.current) setOrders(currentOrders);
    } catch (caughtError) {
      if (version === requestVersion.current) setError(getErrorMessage(caughtError));
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    void getCurrentAccount()
      .then((account) => {
        if (!active) return;
        if (account.role !== "Chef") {
          setAuthorized(false);
          setError("This workspace is available to signed-in chefs only.");
          setLoading(false);
          return;
        }
        setAuthorized(true);
      })
      .catch((caughtError: unknown) => {
        if (active) {
          setAuthorized(false);
          setError(getErrorMessage(caughtError));
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (authorized) void refresh(true);
  }, [authorized, refresh]);

  useEffect(() => {
    if (!authorized) return;
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void refresh();
    }, 5000);
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") void refresh();
    };
    document.addEventListener("visibilitychange", refreshWhenVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", refreshWhenVisible);
    };
  }, [authorized, refresh]);

  return (
    <div className="app-shell admin-shell">
      <aside className="admin-sidebar">
        <Link className="admin-brand" to="/" aria-label="Plate restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">WORKSPACE</div>
        <Link className="admin-sidebar-link" to="/admin/staff">Account and workspaces</Link>
        <Link className="admin-sidebar-link" to="/chef/availability">Dish availability</Link>
        <span className="admin-sidebar-link active" aria-current="page">Kitchen queue</span>
      </aside>

      <main className="page admin-page staff-page order-workspace kitchen-workspace">
        <header className="admin-header">
          <div>
            <p className="eyebrow">KITCHEN / CHEF</p>
            <h1>Kitchen queue</h1>
            <p className="subtitle">Incoming orders grouped by table. Prepare dishes in the sequence shown.</p>
          </div>
          <div className="workspace-session-actions">
            <button type="button" className="secondary-button" disabled={loading || !authorized} onClick={() => void refresh(true)}>
              {loading ? "Refreshing..." : "Refresh queue"}
            </button>
            <WorkspaceSessionActions />
          </div>
        </header>

        {authorized && !loading && !error && (
          <div className="kitchen-queue-summary" aria-live="polite">
            <div><strong>{tableQueues.length}</strong><span>Active tables</span></div>
            <div><strong>{orders.reduce((sum, order) => sum + order.items.reduce((itemSum, item) => itemSum + item.quantity, 0), 0)}</strong><span>Dishes to prepare</span></div>
            <div><strong>{orders.length}</strong><span>Incoming tickets</span></div>
            <span className="kitchen-live-indicator"><i /> Live · updates every 5 seconds</span>
          </div>
        )}

        {error && (
          <div className="admin-error" role="alert">
            <span>{error}</span>
            {authorized && <button type="button" onClick={() => void refresh(true)}>Try again</button>}
          </div>
        )}

        {authorized === false && !loading && (
          <section className="staff-card order-state-card">
            <div className="order-state-icon">!</div>
            <h2>Chef sign-in required</h2>
            <p className="staff-help">Sign in with an active chef account to view kitchen orders.</p>
            <Link className="primary-button" to="/admin/staff">Sign in or switch account</Link>
          </section>
        )}

        {loading && (
          <section className="staff-card kitchen-loading-card" role="status" aria-busy="true">
            <span className="admin-spinner" />
            <strong>Loading kitchen queue</strong>
            <span>Fetching the latest incoming orders...</span>
          </section>
        )}

        {authorized && !loading && !error && orders.length === 0 && (
          <section className="staff-card order-state-card" role="status">
            <div className="order-state-icon order-state-icon-success">✓</div>
            <h2>Kitchen is all caught up</h2>
            <p className="staff-help">There are no incoming orders right now. New tickets will appear here when waiters send them.</p>
          </section>
        )}

        {authorized && !loading && !error && tableQueues.length > 0 && (
          <section className="kitchen-table-grid" aria-label="Kitchen orders grouped by table" aria-live="polite">
            {tableQueues.map((table) => {
              let preparationNumber = 0;
              return (
                <article className="staff-card kitchen-table-card" key={table.tableId}>
                  <header className="kitchen-table-heading">
                    <div className="kitchen-table-title">
                      <span className="kitchen-table-icon">T</span>
                      <div>
                        <p className="eyebrow">DINING TABLE</p>
                        <h2>Table {table.tableId}</h2>
                      </div>
                    </div>
                    <span className="kitchen-table-count">{table.itemCount} {table.itemCount === 1 ? "item" : "items"}</span>
                  </header>

                  <div className="kitchen-table-meta">
                    <span>{table.submissions.length} {table.submissions.length === 1 ? "ticket" : "tickets"}</span>
                    <span>{table.dishCount} dish {table.dishCount === 1 ? "type" : "types"}</span>
                  </div>

                  {table.submissions.map((submission) => (
                    <section className="kitchen-ticket" key={submission.id} aria-label={`Ticket ${submission.id}`}>
                      <header className="kitchen-ticket-heading">
                        <div>
                          <strong>Ticket #{submission.id}</strong>
                          {submission.seatNumber !== null && <span>Seat {submission.seatNumber}</span>}
                        </div>
                        <time dateTime={submission.sentAtUtc}>{new Date(submission.sentAtUtc).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}</time>
                      </header>
                      <ol className="kitchen-preparation-list">
                        {submission.items.map((item) => {
                          preparationNumber += 1;
                          return (
                            <li className="kitchen-preparation-item" key={item.id}>
                              <span className="kitchen-sequence-number">{String(preparationNumber).padStart(2, "0")}</span>
                              <div className="kitchen-dish-details">
                                <div className="kitchen-dish-name-row">
                                  <strong>{item.dishName}</strong>
                                  <span className="kitchen-dish-quantity">× {item.quantity}</span>
                                </div>
                                {item.specialRequest && (
                                  <div className="kitchen-special-request"><span>Special request</span><p>{item.specialRequest}</p></div>
                                )}
                              </div>
                            </li>
                          );
                        })}
                      </ol>
                    </section>
                  ))}
                </article>
              );
            })}
          </section>
        )}
      </main>
    </div>
  );
}

export default KitchenOrdersPage;
