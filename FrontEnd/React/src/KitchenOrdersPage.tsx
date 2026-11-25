import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";
import {
  getCurrentAccount,
  getKitchenOrders,
  updateKitchenItemStatus,
} from "./services/api";
import type { KitchenSubmissionResponse, OrderItemDetailsResponse } from "./services/api";
import WorkspaceSessionActions from "./WorkspaceSessionActions";
import { useKitchenUpdates } from "./services/useKitchenUpdates";
import "./kitchen-dashboard.css";

type QueueFilter = "active" | "new" | "preparing" | "ready" | "all";
type SortMode = "oldest" | "newest";
type TableQueue = {
  tableId: number;
  tableNumber: number | null;
  submissions: KitchenSubmissionResponse[];
  firstSentAt: string;
  newCount: number;
  preparingCount: number;
  readyCount: number;
};

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Could not load kitchen orders.";
}

function isActive(item: OrderItemDetailsResponse): boolean {
  return item.preparationStatus !== "Ready";
}

function matchesFilter(item: OrderItemDetailsResponse, filter: QueueFilter): boolean {
  if (filter === "all") return true;
  if (filter === "active") return isActive(item);
  if (filter === "new") return item.preparationStatus === "New";
  if (filter === "preparing") return item.preparationStatus === "Preparing";
  return item.preparationStatus === "Ready";
}

function tableQueuesFrom(orders: KitchenSubmissionResponse[]): TableQueue[] {
  const groups = new Map<number, KitchenSubmissionResponse[]>();
  for (const submission of orders) {
    groups.set(submission.diningTableId, [...(groups.get(submission.diningTableId) ?? []), submission]);
  }
  return [...groups.entries()].map(([tableId, submissions]) => {
    const sorted = [...submissions].sort((a, b) =>
      Date.parse(a.sentAtUtc) - Date.parse(b.sentAtUtc) || a.id - b.id);
    const items = sorted.flatMap((submission) => submission.items);
    return {
      tableId,
      tableNumber: sorted.find((submission) => Number.isInteger(submission.tableNumber) && (submission.tableNumber ?? 0) > 0)?.tableNumber ?? null,
      submissions: sorted,
      firstSentAt: sorted[0]?.sentAtUtc ?? "",
      newCount: items.filter((item) => item.preparationStatus === "New").reduce((sum, item) => sum + item.quantity, 0),
      preparingCount: items.filter((item) => item.preparationStatus === "Preparing").reduce((sum, item) => sum + item.quantity, 0),
      readyCount: items.filter((item) => item.preparationStatus === "Ready").reduce((sum, item) => sum + item.quantity, 0),
    };
  });
}

function timeLabel(value: string): string {
  const date = new Date(value);
  if (!Number.isFinite(date.getTime())) return "Time unavailable";
  return date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function elapsedLabel(value: string, now: number): string {
  const age = now - Date.parse(value);
  if (!Number.isFinite(age) || age < 0) return "Just received";
  const minutes = Math.floor(age / 60000);
  if (minutes < 1) return "Just received";
  if (minutes < 60) return `${minutes} min waiting`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m waiting`;
}

function KitchenOrdersPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [orders, setOrders] = useState<KitchenSubmissionResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [updatingItemId, setUpdatingItemId] = useState<number | null>(null);
  const [filter, setFilter] = useState<QueueFilter>("active");
  const [sortMode, setSortMode] = useState<SortMode>("oldest");
  const [search, setSearch] = useState("");
  const [clock, setClock] = useState(() => Date.now());
  const [lastUpdatedAt, setLastUpdatedAt] = useState<number | null>(null);
  const requestVersion = useRef(0);
  const statusUpdateInFlight = useRef(false);

  const refresh = useCallback(async (showLoading = false) => {
    const version = ++requestVersion.current;
    if (showLoading) setRefreshing(true);
    try {
      const incoming = await getKitchenOrders();
      if (version === requestVersion.current) {
        setOrders(incoming);
        setLastUpdatedAt(Date.now());
        setError(null);
      }
    } catch (caughtError) {
      if (version === requestVersion.current) setError(getErrorMessage(caughtError));
    } finally {
      if (version === requestVersion.current) {
        setLoading(false);
        setRefreshing(false);
      }
    }
  }, []);

  useEffect(() => {
    let active = true;
    void getCurrentAccount().then((account) => {
      if (!active) return;
      if (account.role !== "Chef") {
        setAuthorized(false);
        setError("This workspace is available to signed-in chefs only.");
        setLoading(false);
        return;
      }
      setAuthorized(true);
    }).catch((caughtError: unknown) => {
      if (active) {
        setAuthorized(false);
        setError(getErrorMessage(caughtError));
        setLoading(false);
      }
    });
    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (!authorized) return;
    void refresh(true);
  }, [authorized, refresh]);


  useKitchenUpdates(() => {
    if (authorized) void refresh();
  });

  useEffect(() => {
    if (!authorized) return;
    const poll = window.setInterval(() => {
      if (document.visibilityState === "visible") void refresh();
    }, 5000);
    const clockTick = window.setInterval(() => setClock(Date.now()), 15000);
    const visible = () => { if (document.visibilityState === "visible") void refresh(); };
    document.addEventListener("visibilitychange", visible);
    return () => {
      window.clearInterval(poll);
      window.clearInterval(clockTick);
      document.removeEventListener("visibilitychange", visible);
    };
  }, [authorized, refresh]);

  const advanceItem = async (itemId: number, nextStatus: "Preparing" | "Ready") => {
    if (statusUpdateInFlight.current) return;
    statusUpdateInFlight.current = true;
    setUpdatingItemId(itemId);
    try {
      const result = await updateKitchenItemStatus(itemId, nextStatus);
      setOrders((current) => current.map((submission) => ({
        ...submission,
        items: submission.items.map((item) => item.id === result.id ? {
          ...item,
          preparationStatus: result.status,
          preparationStartedAtUtc: result.preparationStartedAtUtc,
          readyAtUtc: result.readyAtUtc,
        } : item),
      })));
      await refresh();
    } catch (caughtError) {
      setError(getErrorMessage(caughtError));
    } finally {
      statusUpdateInFlight.current = false;
      setUpdatingItemId(null);
    }
  };

  const tableQueues = useMemo(() => tableQueuesFrom(orders), [orders]);
  const totalNew = tableQueues.reduce((sum, table) => sum + table.newCount, 0);
  const totalPreparing = tableQueues.reduce((sum, table) => sum + table.preparingCount, 0);
  const totalReady = tableQueues.reduce((sum, table) => sum + table.readyCount, 0);
  const activeTables = tableQueues.filter((table) => table.newCount + table.preparingCount > 0).length;
  const filterOptions: { value: QueueFilter; label: string; count: number }[] = [
    { value: "active", label: "In progress", count: totalNew + totalPreparing },
    { value: "new", label: "To start", count: totalNew },
    { value: "preparing", label: "Preparing", count: totalPreparing },
    { value: "ready", label: "Ready", count: totalReady },
    { value: "all", label: "All", count: totalNew + totalPreparing + totalReady },
  ];
  const visibleTables = useMemo(() => {
    const query = search.trim().toLowerCase();
    return tableQueues.map((table) => {
      const number = table.tableNumber;
      const tableMatches = number !== null && (`table ${number}`.includes(query) || String(number) === query);
      const matchingSubmissions = table.submissions.flatMap((submission) => {
        const ticketMatches = `ticket ${submission.id}`.includes(query) || `order ${submission.orderId}`.includes(query);
        const items = submission.items.filter((item) => matchesFilter(item, filter) &&
          (!query || tableMatches || ticketMatches || item.dishName.toLowerCase().includes(query) ||
            item.specialRequest?.toLowerCase().includes(query)));
        return items.length ? [{ ...submission, items }] : [];
      });
      return { ...table, submissions: matchingSubmissions };
    }).filter((table) => table.submissions.length > 0).sort((a, b) => {
      const delta = Date.parse(a.firstSentAt) - Date.parse(b.firstSentAt);
      return sortMode === "oldest" ? delta : -delta;
    });
  }, [tableQueues, filter, search, sortMode]);


  return (
    <main className="page admin-page staff-page order-workspace kitchen-workspace chef-dashboard">
      <header className="admin-header chef-dashboard-header">
        <div>
          <p className="eyebrow">KITCHEN / LIVE SERVICE</p>
          <h1>Kitchen orders</h1>
          <p className="subtitle">Tables first. Tickets in order. Every dish clearly tracked from new to ready.</p>
        </div>
        <div className="workspace-session-actions">
          <button type="button" className="secondary-button" disabled={refreshing || !authorized} onClick={() => void refresh(true)}>
            {refreshing ? "Refreshing…" : "↻ Refresh queue"}
          </button>
          <WorkspaceSessionActions />
        </div>
      </header>

      {authorized && (
        <div className="chef-dashboard-overview" aria-live="polite">
          <div className="chef-stat"><span>Active tables</span><strong>{activeTables}</strong><small>With dishes being prepared</small></div>
          <div className="chef-stat chef-stat-new"><span>To start</span><strong>{totalNew}</strong><small>New portions</small></div>
          <div className="chef-stat chef-stat-preparing"><span>Preparing</span><strong>{totalPreparing}</strong><small>In the kitchen</small></div>
          <div className="chef-stat chef-stat-ready"><span>Ready</span><strong>{totalReady}</strong><small>Completed portions</small></div>
        </div>
      )}

      {authorized && (
        <div className="chef-dashboard-toolbar">
          <div className="chef-board-filters" role="group" aria-label="Filter kitchen dishes">
            {filterOptions.map((option) => (
              <button type="button" key={option.value}
                className={filter === option.value ? "selected" : ""}
                aria-pressed={filter === option.value}
                onClick={() => setFilter(option.value)}>
                {option.label} <span>{option.count}</span>
              </button>
            ))}
          </div>
          <div className="chef-board-search-sort">
            <label className="chef-board-search">
              <span className="sr-only">Search dishes, tickets or table numbers</span>
              <input type="search" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search dishes, tickets, tables…" />
            </label>
            <label className="chef-board-sort"><span className="sr-only">Sort tickets</span>
              <select value={sortMode} onChange={(event) => setSortMode(event.target.value as SortMode)}>
                <option value="oldest">Oldest first</option><option value="newest">Newest first</option>
              </select>
            </label>
          </div>
        </div>
      )}

      {authorized && (
        <div className="chef-map-toolbar">
          <span className="chef-live-indicator"><span aria-hidden="true" /> Live updates every 5s{lastUpdatedAt ? ` · Last sync ${timeLabel(new Date(lastUpdatedAt).toISOString())}` : ""}</span>
          <span className="chef-table-source">Table numbers synced from restaurant settings</span>
        </div>
      )}

      {error && (
        <div className="admin-error" role="alert"><span>{error}</span>
          {authorized && <button type="button" onClick={() => void refresh(true)}>Try again</button>}
        </div>
      )}

      {authorized === false && !loading && (
        <section className="staff-card order-state-card">
          <h2>Chef sign-in required</h2>
          <p className="staff-help">Sign in with an active chef account to view kitchen orders.</p>
          <Link className="primary-button" to="/admin/staff">Sign in or switch account</Link>
        </section>
      )}

      {loading && authorized && (
        <section className="chef-loading-grid" role="status" aria-busy="true" aria-label="Loading kitchen orders">
          {[0, 1, 2].map((index) => <div key={index} className="chef-loading-card"><div /><span /><span /><span /></div>)}
        </section>
      )}

      {authorized && !loading && orders.length === 0 && !error && (
        <section className="staff-card chef-empty-state"><span aria-hidden="true">✓</span><h2>All caught up</h2>
          <p>There are no kitchen tickets yet. New orders will appear here automatically.</p>
        </section>
      )}

      {authorized && !loading && orders.length > 0 && visibleTables.length === 0 && (
        <section className="staff-card chef-empty-state"><h2>No dishes in this view</h2>
          <p>Try another status or clear your search. Completed dishes are in the Ready tab.</p>
          <button type="button" className="secondary-button" onClick={() => { setFilter("all"); setSearch(""); }}>View all dishes</button>
        </section>
      )}

      {authorized && !loading && visibleTables.length > 0 && (
        <section className="chef-table-grid" aria-label="Kitchen tables and tickets">
          {visibleTables.map((table) => {
            const configuredNumber = table.tableNumber;
            const count = table.submissions.reduce((sum, ticket) => sum + ticket.items.reduce((total, item) => total + item.quantity, 0), 0);
            return (
              <article className="chef-table-panel" key={table.tableId}>
                <header className="chef-table-panel-header">
                  <div className="chef-table-identity">
                    <span className="chef-table-glyph" aria-hidden="true">▦</span>
                    <div>
                      <span className="chef-overline">DINING TABLE</span>
                      {configuredNumber !== null
                        ? <h2>Table {configuredNumber}</h2>
                        : <><h2 className="chef-table-unmapped">Number unavailable</h2><small>Reference ID {table.tableId} · not the table number</small></>}
                    </div>
                  </div>
                  <span className="chef-table-count">{count} {count === 1 ? "portion" : "portions"}</span>
                </header>
                <div className="chef-table-subheader">
                  <span>{table.submissions.length} {table.submissions.length === 1 ? "ticket" : "tickets"}</span>
                  <span>{table.newCount} new · {table.preparingCount} preparing · {table.readyCount} ready</span>
                </div>
                <div className="chef-ticket-stack">
                  {table.submissions.map((ticket) => (
                    <section className="chef-ticket-panel" key={ticket.id} aria-label={`Kitchen ticket ${ticket.id}`}>
                      <header className="chef-ticket-header">
                        <div className="chef-ticket-id"><strong>Ticket #{ticket.id}</strong><span>Order #{ticket.orderId}</span></div>
                        <div className="chef-ticket-details">
                          {ticket.seatNumber !== null && <span className="chef-seat-badge">Seat {ticket.seatNumber}</span>}
                          <time dateTime={ticket.sentAtUtc} title={new Date(ticket.sentAtUtc).toLocaleString()}>
                            {timeLabel(ticket.sentAtUtc)} · {elapsedLabel(ticket.sentAtUtc, clock)}
                          </time>
                        </div>
                      </header>
                      <ul className="chef-dish-list">
                        {ticket.items.map((item, index) => (
                          <li className={`chef-dish-row chef-dish-${item.preparationStatus.toLowerCase()}`} key={item.id}>
                            <span className="chef-dish-quantity" aria-label={`Quantity ${item.quantity}`}>{item.quantity}×</span>
                            <div className="chef-dish-description">
                              <div className="chef-dish-title"><strong>{item.dishName}</strong><span className="chef-dish-index">Line {index + 1}</span></div>
                              {item.specialRequest?.trim() && <div className="chef-dish-note"><b>! Special request</b><p>{item.specialRequest}</p></div>}
                            </div>
                            <div className="chef-dish-actions">
                              <span className={`chef-dish-status chef-status-${item.preparationStatus.toLowerCase()}`}>
                                {item.preparationStatus === "Ready" ? "✓ Ready" : item.preparationStatus === "New" ? "● New" : "◷ Preparing"}
                              </span>
                              {item.preparationStatus === "New" && (
                                <button type="button" className="chef-action-start" disabled={updatingItemId !== null} onClick={() => void advanceItem(item.id, "Preparing")}>
                                  {updatingItemId === item.id ? "Starting…" : "Start preparing"}
                                </button>
                              )}
                              {item.preparationStatus === "Preparing" && (
                                <button type="button" className="chef-action-ready" disabled={updatingItemId !== null} onClick={() => void advanceItem(item.id, "Ready")}>
                                  {updatingItemId === item.id ? "Saving…" : "✓ Mark ready"}
                                </button>
                              )}
                            </div>
                          </li>
                        ))}
                      </ul>
                    </section>
                  ))}
                </div>
              </article>
            );
          })}
        </section>
      )}
    </main>
  );
}

export default KitchenOrdersPage;
