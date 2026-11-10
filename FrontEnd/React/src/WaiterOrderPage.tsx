import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  addOrderItem,
  getCurrentAccount,
  getMenuDishes,
  getOrderDetails,
  getWaiterTables,
  openOrCreateOrder,
  removeOrderItem,
  sendOrder,
  updateOrderItem,
} from "./services/api";
import type {
  MenuDishResponse,
  OrderDetailsResponse,
  OrderItemDetailsResponse,
  WaiterTableResponse,
} from "./services/api";
import { formatPrice } from "./services/currency";
import { useMenuUpdates } from "./services/useMenuUpdates";

type ItemDraft = {
  quantity: string;
  specialRequest: string;
};

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function WaiterOrderPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [tables, setTables] = useState<WaiterTableResponse[]>([]);
  const [dishes, setDishes] = useState<MenuDishResponse[]>([]);
  const [order, setOrder] = useState<OrderDetailsResponse | null>(null);
  const [selectedTableId, setSelectedTableId] = useState<number | null>(null);
  const [selectedDishId, setSelectedDishId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [specialRequest, setSpecialRequest] = useState("");
  const [itemDrafts, setItemDrafts] = useState<Record<number, ItemDraft>>({});
  const [loading, setLoading] = useState(true);
  const [opening, setOpening] = useState(false);
  const [savingItem, setSavingItem] = useState(false);
  const [busyItemId, setBusyItemId] = useState<number | null>(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const operationLock = useRef(false);
  const total = useMemo(
    () => order?.items.reduce((sum, item) => {
      const currentQuantity = order.status === "Open"
        ? Number(itemDrafts[item.id]?.quantity ?? item.quantity)
        : item.quantity;
      return sum + item.unitPrice * (
        Number.isSafeInteger(currentQuantity) && currentQuantity > 0
          ? currentQuantity
          : 0
      );
    }, 0) ?? 0,
    [itemDrafts, order],
  );
  const availableDishes = useMemo(
    () => dishes.filter((dish) => dish.isAvailable),
    [dishes],
  );

  const refreshDishes = useCallback(async () => {
    try {
      setDishes(await getMenuDishes());
    } catch (caughtError) {
      setError((current) => current
        ? `${current} Menu refresh failed: ${getErrorMessage(caughtError, "Could not reload dishes.")}`
        : `Could not refresh the menu: ${getErrorMessage(caughtError, "Please try again.")}`);
    }
  }, []);

  useEffect(() => {
    let active = true;

    const loadWorkspace = async () => {
      try {
        const [account, assignedTables] = await Promise.all([
          getCurrentAccount(),
          getWaiterTables(),
        ]);
        if (!active) return;
        if (account.role !== "Waiter") {
          setAuthorized(false);
          setError("This workspace is available to signed-in waiters only.");
          return;
        }
        setAuthorized(true);
        setTables(assignedTables);
        try {
          const menu = await getMenuDishes();
          if (active) {
            setDishes(menu);
            setSelectedDishId(String(menu.find((dish) => dish.isAvailable)?.id ?? ""));
          }
        } catch (caughtError) {
          if (active) {
            setError(`Could not load available dishes: ${getErrorMessage(caughtError, "Please try again.")}`);
          }
        }
      } catch (caughtError: unknown) {
        if (active) {
          setAuthorized(false);
          setError(getErrorMessage(caughtError, "Could not load the waiter workspace."));
        }
      } finally {
        if (active) setLoading(false);
      }
    };
    void loadWorkspace();

    return () => {
      active = false;
    };
  }, []);

  useMenuUpdates(() => {
    if (authorized) void refreshDishes();
  });

  const refreshOrder = useCallback(async (orderId: number): Promise<OrderDetailsResponse> => {
    const updatedOrder = await getOrderDetails(orderId);
    setOrder(updatedOrder);
    setItemDrafts(Object.fromEntries(updatedOrder.items.map((item) => [
      item.id,
      { quantity: String(item.quantity), specialRequest: item.specialRequest ?? "" },
    ])));
    return updatedOrder;
  }, []);

  const openTable = async (table: WaiterTableResponse) => {
    if (operationLock.current) return;
    operationLock.current = true;
    setOpening(true);
    setError(null);
    setNotice(null);
    try {
      const opened = await openOrCreateOrder(table.id);
      await refreshOrder(opened.orderId);
      setSelectedTableId(table.id);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not open this table's order."));
    } finally {
      operationLock.current = false;
      setOpening(false);
    }
  };

  const addItem = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order || operationLock.current) return;
    const dishId = Number(selectedDishId);
    const parsedQuantity = Number(quantity);
    if (!Number.isSafeInteger(dishId) || dishId <= 0 ||
        !Number.isSafeInteger(parsedQuantity) || parsedQuantity <= 0) {
      setError("Choose an available dish and enter a positive whole-number quantity.");
      return;
    }

    operationLock.current = true;
    setSavingItem(true);
    setError(null);
    setNotice(null);
    try {
      await addOrderItem(order.id, dishId, parsedQuantity, specialRequest);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not add this dish. Refresh the order and try again."));
      try {
        await refreshOrder(order.id);
      } catch {
        setNotice("The add request may have reached the server, but the order could not be reloaded. Refresh the order before retrying.");
      }
      operationLock.current = false;
      setSavingItem(false);
      return;
    }

    setSpecialRequest("");
    setQuantity("1");
    try {
      await refreshOrder(order.id);
    } catch (caughtError) {
      setNotice(`Dish added, but the order could not be reloaded: ${getErrorMessage(caughtError, "Please refresh the order.")}`);
    } finally {
      operationLock.current = false;
      setSavingItem(false);
    }
  };

  const saveItem = async (
    item: OrderItemDetailsResponse,
    draft: ItemDraft,
  ) => {
    if (!order || operationLock.current) return;
    const parsedQuantity = Number(draft.quantity);
    if (!Number.isSafeInteger(parsedQuantity) || parsedQuantity <= 0) {
      setError("Quantity must be a positive whole number.");
      return;
    }

    operationLock.current = true;
    setBusyItemId(item.id);
    setError(null);
    setNotice(null);
    try {
      await updateOrderItem(order.id, item.id, parsedQuantity, draft.specialRequest);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not update this dish."));
      try {
        await refreshOrder(order.id);
      } catch {
        setNotice("The saved order could not be reloaded. Retry the refresh before editing again.");
      }
      operationLock.current = false;
      setBusyItemId(null);
      return;
    }
    try {
      await refreshOrder(order.id);
    } catch (caughtError) {
      setNotice(`Dish updated, but the order could not be reloaded: ${getErrorMessage(caughtError, "Please refresh the order.")}`);
    } finally {
      operationLock.current = false;
      setBusyItemId(null);
    }
  };

  const deleteItem = async (itemId: number) => {
    if (!order || operationLock.current) return;
    operationLock.current = true;
    setBusyItemId(itemId);
    setError(null);
    setNotice(null);
    try {
      await removeOrderItem(order.id, itemId);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not remove this dish."));
      try {
        await refreshOrder(order.id);
      } catch {
        setNotice("The saved order could not be reloaded. Retry the refresh before editing again.");
      }
      operationLock.current = false;
      setBusyItemId(null);
      return;
    }
    try {
      await refreshOrder(order.id);
    } catch (caughtError) {
      setNotice(`Dish removed, but the order could not be reloaded: ${getErrorMessage(caughtError, "Please refresh the order.")}`);
    } finally {
      operationLock.current = false;
      setBusyItemId(null);
    }
  };

  const submitOrder = async () => {
    if (!order || operationLock.current) return;
    operationLock.current = true;
    setSending(true);
    setError(null);
    setNotice(null);
    try {
      const result = await sendOrder(order.id);
      setOrder(result.kitchenOrder);
      setNotice(result.alreadySent
        ? "This order was already sent; the kitchen has one copy."
        : "Order sent to the kitchen.");
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not send this order."));
      try {
        const refreshedOrder = await refreshOrder(order.id);
        if (refreshedOrder.status === "Sent") {
          setOrder(refreshedOrder);
          setError(null);
          setNotice("The order was sent despite the interrupted response. The kitchen has one copy.");
        }
      } catch {
        setNotice("The order status could not be confirmed. Refresh before trying again; sending is safe to retry.");
      }
    } finally {
      operationLock.current = false;
      setSending(false);
    }
  };

  const activeTable = tables.find((table) => table.id === selectedTableId);

  const reloadOrder = async () => {
    if (!order || operationLock.current) return;
    operationLock.current = true;
    setError(null);
    setNotice(null);
    try {
      await refreshOrder(order.id);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not reload the order."));
    } finally {
      operationLock.current = false;
    }
  };

  return (
    <div className="app-shell admin-shell">
      <aside className="admin-sidebar">
        <Link className="admin-brand" to="/" aria-label="Plate restaurant customer menu">
          <strong>PLATE</strong>
          <span>RESTAURANT OPERATIONS</span>
        </Link>
        <div className="admin-sidebar-label">WORKSPACE</div>
        <Link className="admin-sidebar-link" to="/admin/staff">Account and workspaces</Link>
        <Link className="admin-sidebar-link" to="/">Customer menu</Link>
      </aside>

      <main className="page admin-page staff-page order-workspace">
        <header className="admin-header">
          <div>
            <p className="eyebrow">FLOOR / WAITER</p>
            <h1>Table orders</h1>
            <p className="subtitle">Create and update a table order. Its subtotal changes as quantities change.</p>
          </div>
          <Link className="secondary-button" to="/admin/staff">Account</Link>
        </header>

        {error && <div className="admin-error" role="alert">{error}</div>}
        {notice && <div className="admin-success" role="status">{notice}</div>}

        {loading ? (
          <div className="admin-loading" role="status" aria-busy="true">
            <span className="admin-spinner" /> Loading waiter workspace...
          </div>
        ) : !authorized ? (
          <section className="staff-card">
            <h2>Waiter sign-in required</h2>
            <p className="staff-help">Sign in with an active waiter account to view assigned tables.</p>
            <Link className="primary-button" to="/admin/staff">Sign in or switch account</Link>
          </section>
        ) : (
          <>
            <section className="staff-card">
              <div className="staff-card-heading">
                <div><p className="eyebrow">ASSIGNED TABLES</p><h2>Choose a table</h2></div>
              </div>
              {tables.length === 0 ? (
                <p className="staff-help">No active tables are assigned to your waiter account.</p>
              ) : (
                <div className="order-table-list">
                  {tables.map((table) => (
                    <button
                      type="button"
                      key={table.id}
                      className={`order-table-button${selectedTableId === table.id ? " selected" : ""}`}
                      disabled={opening}
                      onClick={() => void openTable(table)}
                    >
                      <strong>Table {table.tableNumber}</strong>
                      <span>{table.seatCount} seats</span>
                    </button>
                  ))}
                </div>
              )}
            </section>

            {order && activeTable && (
              <section className="staff-card order-composer" aria-labelledby="order-heading">
                <div className="staff-card-heading">
                  <div>
                    <p className="eyebrow">TABLE {activeTable.tableNumber} / ORDER #{order.id}</p>
                    <h2 id="order-heading">{order.status === "Sent" ? "Sent to kitchen" : "Order draft"}</h2>
                  </div>
                  <div className="order-header-actions">
                    <button
                      type="button"
                      className="secondary-button"
                      disabled={opening || savingItem || sending || busyItemId !== null}
                      onClick={() => void reloadOrder()}
                    >
                      Reload order
                    </button>
                    {order.status === "Sent" && (
                      <button
                        type="button"
                        className="secondary-button"
                        disabled={opening}
                        onClick={() => void openTable(activeTable)}
                      >
                        {opening ? "Opening..." : "Start another order"}
                      </button>
                    )}
                  </div>
                </div>

                {order.items.length === 0 ? (
                  <p className="staff-help">No dishes added yet. Add at least one dish before sending.</p>
                ) : (
                  <div className="order-items">
                    {order.items.map((item) => {
                      const draft = itemDrafts[item.id] ?? {
                        quantity: String(item.quantity),
                        specialRequest: item.specialRequest ?? "",
                      };
                      return (
                        <form
                          className="order-item"
                          key={item.id}
                          onSubmit={(event) => {
                            event.preventDefault();
                            void saveItem(item, draft);
                          }}
                        >
                          <div className="order-item-title">
                            <strong>{item.dishName}</strong>
                            <span>{formatPrice(item.unitPrice)} each</span>
                          </div>
                          <label>
                            Quantity
                            <input
                              type="number"
                              min="1"
                              step="1"
                              required
                              disabled={order.status !== "Open" || busyItemId === item.id || savingItem || sending}
                              value={draft.quantity}
                              onChange={(event) => setItemDrafts((current) => ({
                                ...current,
                                [item.id]: { ...draft, quantity: event.target.value },
                              }))}
                            />
                          </label>
                          <label>
                            Special request
                            <input
                              maxLength={500}
                              disabled={order.status !== "Open" || busyItemId === item.id || savingItem || sending}
                              value={draft.specialRequest}
                              onChange={(event) => setItemDrafts((current) => ({
                                ...current,
                                [item.id]: { ...draft, specialRequest: event.target.value },
                              }))}
                            />
                          </label>
                          <span className="order-line-total">
                            {formatPrice(item.unitPrice * (
                              Number.isSafeInteger(Number(draft.quantity)) && Number(draft.quantity) > 0
                                ? Number(draft.quantity)
                                : 0
                            ))}
                          </span>
                          {order.status === "Open" && (
                            <div className="order-item-actions">
                              <button
                                type="submit"
                                className="secondary-button"
                                disabled={busyItemId !== null || savingItem || sending}
                              >
                                {busyItemId === item.id ? "Saving..." : "Save"}
                              </button>
                              <button
                                type="button"
                                className="order-remove-button"
                                disabled={busyItemId !== null || savingItem || sending}
                                onClick={() => void deleteItem(item.id)}
                              >
                                Remove
                              </button>
                            </div>
                          )}
                        </form>
                      );
                    })}
                  </div>
                )}

                {order.status === "Open" && (
                  <>
                    <form className="order-add-form" onSubmit={(event) => void addItem(event)}>
                      <h3>Add a dish</h3>
                      {availableDishes.length === 0 ? (
                        <div className="staff-help">
                          <p>There are no currently available dishes.</p>
                          <button type="button" className="secondary-button" onClick={() => void refreshDishes()}>
                            Reload menu
                          </button>
                        </div>
                      ) : (
                        <>
                          <label>
                            Dish
                            <select
                              required
                              disabled={savingItem || sending}
                              value={selectedDishId}
                              onChange={(event) => setSelectedDishId(event.target.value)}
                            >
                              {availableDishes.map((dish) => (
                                <option key={dish.id} value={dish.id}>
                                  {dish.name} — {formatPrice(dish.price)}
                                </option>
                              ))}
                            </select>
                          </label>
                          <label>
                            Quantity
                            <input
                              type="number"
                              min="1"
                              step="1"
                              required
                              disabled={savingItem || sending}
                              value={quantity}
                              onChange={(event) => setQuantity(event.target.value)}
                            />
                          </label>
                          <label className="order-special-request">
                            Special request
                            <input
                              maxLength={500}
                              disabled={savingItem || sending}
                              value={specialRequest}
                              onChange={(event) => setSpecialRequest(event.target.value)}
                            />
                          </label>
                          <button
                            type="submit"
                            className="primary-button"
                            disabled={savingItem || sending || availableDishes.length === 0}
                          >
                            {savingItem ? "Adding..." : "Add dish"}
                          </button>
                        </>
                      )}
                    </form>
                    <div className="order-total">
                      <span>Subtotal</span>
                      <strong>{formatPrice(total)}</strong>
                    </div>
                    <p className="order-fee-note">Subtotal only; no additional service fee is applied.</p>
                    <button
                      type="button"
                      className="primary-button order-send-button"
                      disabled={order.items.length === 0 || sending || savingItem || busyItemId !== null}
                      onClick={() => void submitOrder()}
                    >
                      {sending ? "Sending..." : "Send order to kitchen"}
                    </button>
                  </>
                )}
                {order.status === "Sent" && (
                  <div className="order-total">
                    <span>Sent subtotal</span>
                    <strong>{formatPrice(total)}</strong>
                  </div>
                )}
              </section>
            )}
          </>
        )}
      </main>
    </div>
  );
}

export default WaiterOrderPage;
