import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { getCurrentAccount, getKitchenOrders } from "./services/api";
import type { OrderDetailsResponse } from "./services/api";
import { formatPrice } from "./services/currency";

function getErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : "Could not load kitchen orders.";
}

function KitchenOrdersPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [orders, setOrders] = useState<OrderDetailsResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const requestVersion = useRef(0);

  const refresh = useCallback(async (showLoading = false) => {
    const version = ++requestVersion.current;
    if (showLoading) setLoading(true);
    setError(null);
    try {
      const currentOrders = await getKitchenOrders();
      if (version === requestVersion.current) {
        setOrders(currentOrders);
      }
    } catch (caughtError) {
      if (version === requestVersion.current) {
        setError(getErrorMessage(caughtError));
      }
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
      </aside>

      <main className="page admin-page staff-page order-workspace">
        <header className="admin-header">
          <div>
            <p className="eyebrow">KITCHEN / CHEF</p>
            <h1>Incoming orders</h1>
            <p className="subtitle">New tickets appear after a waiter sends an order. Reconnecting reloads the durable kitchen feed.</p>
          </div>
          <button
            type="button"
            className="secondary-button"
            disabled={loading || !authorized}
            onClick={() => void refresh(true)}
          >
            {loading ? "Refreshing..." : "Refresh"}
          </button>
        </header>

        {error && (
          <div className="admin-error" role="alert">
            <span>{error}</span>
            {authorized && <button type="button" onClick={() => void refresh(true)}>Try again</button>}
          </div>
        )}
        {authorized === false && !loading && (
          <section className="staff-card">
            <h2>Chef sign-in required</h2>
            <p className="staff-help">Sign in with an active chef account to view kitchen orders.</p>
            <Link className="primary-button" to="/admin/staff">Sign in or switch account</Link>
          </section>
        )}
        {loading && <div className="admin-loading" role="status" aria-busy="true">Loading kitchen orders...</div>}
        {authorized && !loading && !error && orders.length === 0 && (
          <section className="staff-card order-empty" role="status">
            <h2>No incoming orders</h2>
            <p className="staff-help">Sent orders will appear here.</p>
          </section>
        )}
        {authorized && orders.length > 0 && (
          <section className="kitchen-order-list" aria-label="Incoming kitchen orders" aria-live="polite">
            {orders.map((order) => {
              const subtotal = order.items.reduce(
                (sum, item) => sum + item.unitPrice * item.quantity,
                0,
              );
              return (
                <article className="staff-card kitchen-order-card" key={order.id}>
                  <header className="kitchen-order-heading">
                    <div>
                      <p className="eyebrow">TABLE ID {order.diningTableId}</p>
                      <h2>Order #{order.id}</h2>
                    </div>
                    <span className="kitchen-order-status">{order.status}</span>
                  </header>
                  <p className="kitchen-order-time">
                    Sent {order.sentAtUtc ? new Date(order.sentAtUtc).toLocaleString() : "time unavailable"}
                  </p>
                  <ul className="kitchen-order-items">
                    {order.items.map((item) => (
                      <li key={item.id}>
                        <div>
                          <strong>{item.quantity} × {item.dishName}</strong>
                          {item.specialRequest && <p>Request: {item.specialRequest}</p>}
                        </div>
                        <span>{formatPrice(item.unitPrice * item.quantity)}</span>
                      </li>
                    ))}
                  </ul>
                  <footer className="kitchen-order-total">
                    <span>Subtotal</span>
                    <strong>{formatPrice(subtotal)}</strong>
                  </footer>
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
