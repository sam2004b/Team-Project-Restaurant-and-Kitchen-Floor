import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import {
  getOrderHistoryDetails,
  getOrderHistoryPage,
  getRestaurantStaff,
} from "./services/api";
import type {
  OrderHistoryDetailsResponse,
  OrderHistorySummaryResponse,
  StaffMemberResponse,
} from "./services/api";
import { Link, useLocation } from "react-router-dom";
import { useAuth } from "./services/AuthContext";
import "./admin-order-history.css";

const pageSize = 20;
const statuses = ["All", "Open", "Sent", "Closed", "Cancelled", "Legacy"] as const;

function dateLabel(value: string | null | undefined): string {
  if (!value) return "—";
  const time = new Date(value);
  return Number.isNaN(time.getTime()) ? "—" : time.toLocaleString();
}

function money(amount: number | null | undefined, currency?: string | null): string {
  if (amount == null) return "Not billed";
  return `${amount.toFixed(2)} ${currency || ""}`.trim();
}

function readableJson(json: string | null | undefined): string | null {
  if (!json) return null;
  try {
    return JSON.stringify(JSON.parse(json), null, 2);
  } catch {
    return json;
  }
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Could not load order history.";
}

export default function AdminOrderHistoryPage() {
  const { account } = useAuth();
  const { pathname } = useLocation();
  const waiterView = pathname === "/admin/waiter-order-history";
  const restaurantId = Number(account?.restaurantId);
  const [page, setPage] = useState(1);
  const [searchDraft, setSearchDraft] = useState("");
  const [search, setSearch] = useState("");
  const [tableDraft, setTableDraft] = useState("");
  const [table, setTable] = useState<number | undefined>();
  const [status, setStatus] = useState<(typeof statuses)[number]>("All");
  const [selectedWaiterId, setSelectedWaiterId] = useState<number | null>(null);
  const [result, setResult] = useState<OrderHistorySummaryResponse | null>(null);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [details, setDetails] = useState<OrderHistoryDetailsResponse | null>(null);
  const [staff, setStaff] = useState<StaffMemberResponse[]>([]);
  const [staffError, setStaffError] = useState(false);
  const [loading, setLoading] = useState(true);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const searchVersion = useRef(0);
  const detailsVersion = useRef(0);

  const staffNames = useMemo(() => new Map(staff.map((member) => [member.id, member.userName])), [staff]);
  const waiters = useMemo(() => staff.filter((member) => member.role === "Waiter")
    .sort((a, b) => a.userName.localeCompare(b.userName)), [staff]);
  const actorName = (staffId: number | null | undefined): string => {
    if (!staffId) return "Unknown staff";
    return staffNames.get(staffId) || `Staff #${staffId}`;
  };

  const loadPage = useCallback(async () => {
    const version = ++searchVersion.current;
    setLoading(true);
    setError(null);
    try {
      const response = await getOrderHistoryPage({
        page, pageSize, search: search || undefined, tableNumber: table,
        status: status === "All" ? undefined : status,
        createdByStaffMembershipId: waiterView ? selectedWaiterId ?? undefined : undefined,
      });
      if (version === searchVersion.current) setResult(response);
    } catch (caught) {
      if (version === searchVersion.current) setError(errorMessage(caught));
    } finally {
      if (version === searchVersion.current) setLoading(false);
    }
  }, [page, search, status, table, selectedWaiterId, waiterView]);

  useEffect(() => { void loadPage(); }, [loadPage]);

  useEffect(() => {
    setPage(1);
    setSelectedId(null);
    setDetails(null);
  }, [pathname]);

  useEffect(() => {
    let current = true;
    if (!Number.isInteger(restaurantId) || restaurantId < 1) {
      setStaffError(true);
      return;
    }
    void getRestaurantStaff(restaurantId).then((data) => {
      if (current) setStaff(data);
    }).catch(() => {
      if (current) setStaffError(true);
    });
    return () => { current = false; };
  }, [restaurantId]);

  const selectOrder = useCallback(async (orderId: number) => {
    const version = ++detailsVersion.current;
    setSelectedId(orderId);
    setDetails(null);
    setDetailsError(null);
    setDetailsLoading(true);
    try {
      const response = await getOrderHistoryDetails(orderId);
      if (response.id !== orderId) throw new Error("The API returned the wrong order.");
      if (version === detailsVersion.current) setDetails(response);
    } catch (caught) {
      if (version === detailsVersion.current) setDetailsError(errorMessage(caught));
    } finally {
      if (version === detailsVersion.current) setDetailsLoading(false);
    }
  }, []);

  function applyFilters(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedTable = tableDraft.trim();
    if (trimmedTable && (!/^\d+$/.test(trimmedTable) || Number(trimmedTable) < 1)) {
      setError("Enter a positive table number.");
      return;
    }
    setTable(trimmedTable ? Number(trimmedTable) : undefined);
    setSearch(searchDraft.trim());
    setPage(1);
  }

  return (
    <main className="page admin-page staff-page admin-order-history-page">
      <header className="admin-header">
        <div>
          <p className="eyebrow">RESTAURANT OPERATIONS / AUDIT</p>
          <h1>{waiterView ? "Waiter order history" : "Order history"}</h1>
          <p className="subtitle">Review every order in your restaurant, its table, the waiter who created it, and its recorded activity.</p>
        </div>
        <button className="secondary-button" type="button" disabled={loading} onClick={() => void loadPage()}>
          {loading ? "Refreshing..." : "Refresh history"}
        </button>
      </header>

      <nav className="admin-history-tabs" aria-label="Order history navigation">
        <Link to="/admin/order-history" className={!waiterView ? "active" : ""} aria-current={!waiterView ? "page" : undefined}>All orders</Link>
        <Link to="/admin/waiter-order-history" className={waiterView ? "active" : ""} aria-current={waiterView ? "page" : undefined}>Orders by waiter</Link>
      </nav>

      <section className="admin-history-overview" aria-label="History summary">
        <div><span>Total matching orders</span><strong>{result?.totalCount ?? "—"}</strong></div>
        <div><span>Current page</span><strong>{result?.page ?? page} / {Math.max(1, result?.totalPages ?? 1)}</strong></div>
        <div><span>Restaurant</span><strong>{Number.isInteger(restaurantId) && restaurantId > 0 ? `#${restaurantId}` : "Current"}</strong></div>
      </section>

      {waiterView && (
        <section className="admin-history-waiter-selection" aria-label="Select waiter">
          <label htmlFor="admin-history-waiter">Show orders created by</label>
          <select id="admin-history-waiter" value={selectedWaiterId ?? "all"}
            onChange={(event) => {
              setSelectedWaiterId(event.target.value === "all" ? null : Number(event.target.value));
              setPage(1);
              setSelectedId(null);
              setDetails(null);
            }}>
            <option value="all">All waiters</option>
            {waiters.map((member) => <option key={member.id} value={member.id}>{member.userName}</option>)}
          </select>
          <span>Results are filtered by the backend across the entire restaurant history, before pagination.</span>
        </section>
      )}

      <form className="admin-history-filters" onSubmit={applyFilters} aria-label="Filter orders">
        <label>Find order ID
          <input inputMode="numeric" placeholder="e.g. 42" value={searchDraft} onChange={(event) => setSearchDraft(event.target.value)} />
        </label>
        <label>Table number
          <input inputMode="numeric" placeholder="All tables" value={tableDraft} onChange={(event) => setTableDraft(event.target.value)} />
        </label>
        <label>Order status
          <select value={status} onChange={(event) => { setStatus(event.target.value as (typeof statuses)[number]); setPage(1); }}>
            {statuses.map((item) => <option key={item} value={item}>{item === "All" ? "All statuses" : item}</option>)}
          </select>
        </label>
        <button className="primary-button" type="submit">Apply filters</button>
      </form>

      {staffError && <p className="admin-history-info" role="status">Staff names are unavailable for this session. Membership IDs will be shown instead.</p>}
      {error && <p className="staff-notice error" role="alert">{error}</p>}
      <div className="admin-history-layout">
        <section className="admin-history-list" aria-label="Orders">
          <div className="admin-history-list-heading"><strong>Orders</strong><span>{loading ? "Loading..." : `${result?.items.length ?? 0} on this page`}</span></div>
          {loading && !result && <div className="admin-history-placeholder" role="status">Loading order history...</div>}
          {!loading && !error && result?.items.length === 0 && <div className="admin-history-placeholder">No orders match these filters.</div>}
          {result?.items.map((entry) => (
            <button key={entry.id} type="button"
              className={`admin-history-order${selectedId === entry.id ? " selected" : ""}`}
              onClick={() => void selectOrder(entry.id)} aria-pressed={selectedId === entry.id}>
              <div className="admin-history-order-top"><strong>Order #{entry.id}</strong><span className={`admin-history-status status-${entry.status.toLowerCase()}`}>{entry.status}</span></div>
              <div className="admin-history-order-info">
                <span>Table {entry.tableNumber}{entry.seatNumber == null ? "" : ` · Seat ${entry.seatNumber}`}</span>
                <span>Waiter: {entry.createdByStaffName || actorName(entry.createdByStaffMembershipId)}</span>
              </div>
              <div className="admin-history-order-bottom"><time dateTime={entry.createdAtUtc}>{dateLabel(entry.createdAtUtc)}</time><strong>{money(entry.totalAmount, entry.currencyCode)}</strong></div>
            </button>
          ))}
          <div className="admin-history-pagination">
            <button type="button" className="secondary-button" disabled={loading || page <= 1} onClick={() => setPage((value) => value - 1)}>Previous</button>
            <span>Page {page}</span>
            <button type="button" className="secondary-button" disabled={loading || !result || page >= result.totalPages} onClick={() => setPage((value) => value + 1)}>Next</button>
          </div>
        </section>
        <section className="admin-history-details" aria-label="Selected order details">
          {selectedId == null && <div className="admin-history-placeholder"><strong>Select an order</strong><p>Choose a record to see its dishes, original waiter and audit timeline.</p></div>}
          {detailsLoading && <div className="admin-history-placeholder" role="status">Loading order #{selectedId}...</div>}
          {detailsError && <p className="staff-notice error" role="alert">{detailsError}</p>}
          {details && !detailsLoading && (
            <>
              <div className="admin-history-details-header">
                <div><p className="eyebrow">ORDER RECORD</p><h2>Order #{details.id}</h2><span>Table {details.tableNumber}{details.seatNumber == null ? "" : ` · Seat ${details.seatNumber}`}</span></div>
                <span className={`admin-history-status status-${details.status.toLowerCase()}`}>{details.status}</span>
              </div>
              <dl className="admin-history-facts">
                <div><dt>Created by waiter</dt><dd>{details.createdByStaffName || actorName(details.createdByStaffMembershipId)}</dd></div>
                <div><dt>Created</dt><dd>{dateLabel(details.createdAtUtc)}</dd></div>
                <div><dt>Sent to kitchen</dt><dd>{dateLabel(details.sentAtUtc)}</dd></div>
                <div><dt>Paid</dt><dd>{dateLabel(details.paidAtUtc)}</dd></div>
              </dl>
              <div className="admin-history-section-title"><h3>Dishes</h3><span>{details.items.length} order lines</span></div>
              <div className="admin-history-dishes">
                {details.items.length === 0 ? <p className="staff-help">No surviving item lines. Check the audit timeline for previous changes.</p> : details.items.map((item) => (
                  <div key={item.id} className="admin-history-dish">
                    <div><strong>{item.dishName}</strong><small>× {item.quantity}{item.specialRequest ? ` · ${item.specialRequest}` : ""}</small></div>
                    <div><span>{money(item.lineTotal)}</span><small>{item.serviceStatus} · {item.preparationStatus}</small></div>
                  </div>
                ))}
              </div>
              {details.bill && <div className="admin-history-bill"><span>Bill total</span><strong>{money(details.bill.totalAmount, details.bill.currencyCode)}</strong><span>{details.bill.paymentStatus}</span></div>}
              <div className="admin-history-section-title"><h3>Activity & audit trail</h3><span>{details.timeline.length} events</span></div>
              {details.timeline.length === 0 ? <p className="staff-help">No history events have been recorded for this order yet.</p> : (
                <ol className="admin-history-timeline">
                  {details.timeline.map((event, index) => (
                    <li key={`${event.source}-${event.id}-${index}`}>
                      <div className="admin-history-event-heading"><strong>{event.eventType}</strong><time dateTime={event.occurredAtUtc}>{dateLabel(event.occurredAtUtc)}</time></div>
                      <p>By {actorName(event.actorStaffMembershipId)}{event.orderItemId ? ` · Item #${event.orderItemId}` : ""}</p>
                      {event.reason && <p className="admin-history-reason">Reason: {event.reason}</p>}
                      {(event.beforeValueJson || event.afterValueJson) && (
                        <details className="admin-history-snapshots"><summary>View recorded change</summary>
                          {event.beforeValueJson && <div><strong>Before</strong><pre>{readableJson(event.beforeValueJson)}</pre></div>}
                          {event.afterValueJson && <div><strong>After</strong><pre>{readableJson(event.afterValueJson)}</pre></div>}
                        </details>
                      )}
                    </li>
                  ))}
                </ol>
              )}
            </>
          )}
        </section>
      </div>
    </main>
  );
}
