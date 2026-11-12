import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  ApiError,
  addOrderItem,
  getCurrentAccount,
  getMenuDishes,
  getOrderDetails,
  getWaiterOrderHistory,
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
  WaiterOrderHistoryEntry,
  WaiterTableResponse,
} from "./services/api";
import { formatPrice } from "./services/currency";
import { useMenuUpdates } from "./services/useMenuUpdates";
import WorkspaceSessionActions from "./WorkspaceSessionActions";

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
  const [orderHistory, setOrderHistory] = useState<WaiterOrderHistoryEntry[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [selectedTableId, setSelectedTableId] = useState<number | null>(null);
  const [selectedDishId, setSelectedDishId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [specialRequest, setSpecialRequest] = useState("");
  const [itemDrafts, setItemDrafts] = useState<Record<number, ItemDraft>>({});
  const [loading, setLoading] = useState(true);
  const [refreshingTables, setRefreshingTables] = useState(false);
  const [assignmentsUnavailable, setAssignmentsUnavailable] = useState(false);
  const [opening, setOpening] = useState(false);
  const [savingItem, setSavingItem] = useState(false);
  const [busyItemId, setBusyItemId] = useState<number | null>(null);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const operationLock = useRef(false);
  const orderIsActive = order !== null && (order.status === "Open" || order.status === "Sent");
  const pendingItems = order?.items.filter((item) => item.kitchenSubmissionId === null) ?? [];
  const total = useMemo(
    () => order?.items.reduce((sum, item) => {
      const currentQuantity = itemDrafts[item.id]
        ? Number(itemDrafts[item.id].quantity)
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

  const refreshOrderHistory = useCallback(async (showLoading = true) => {
    if (showLoading) setHistoryLoading(true);
    setHistoryError(null);
    try {
      const history = await getWaiterOrderHistory();
      setOrderHistory(history.sort(
        (first, second) => Date.parse(second.createdAtUtc) - Date.parse(first.createdAtUtc),
      ));
    } catch (caughtError) {
      setHistoryError(getErrorMessage(caughtError, "Could not load order history."));
    } finally {
      if (showLoading) setHistoryLoading(false);
    }
  }, []);

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
        void refreshOrderHistory();
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
          setAssignmentsUnavailable(true);
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
  }, [refreshOrderHistory]);

  useMenuUpdates(() => {
    if (authorized) void refreshDishes();
  });

  const refreshOrder = useCallback(async (
    orderId: number,
    expectedTableId: number,
  ): Promise<OrderDetailsResponse> => {
    let updatedOrder: OrderDetailsResponse;
    try {
      updatedOrder = await getOrderDetails(orderId);
    } catch (caughtError) {
      if (caughtError instanceof ApiError &&
          [401, 403, 404].includes(caughtError.status)) {
        setSelectedTableId(null);
        setOrder(null);
        setItemDrafts({});
        if (caughtError.status === 401 || caughtError.status === 403) {
          setTables([]);
          setAssignmentsUnavailable(true);
        }
      }
      throw caughtError;
    }

    if (updatedOrder.diningTableId !== expectedTableId) {
      setSelectedTableId(null);
      setOrder(null);
      setItemDrafts({});
      throw new Error("The returned order does not belong to the selected table.");
    }

    setOrder(updatedOrder);
    setItemDrafts(Object.fromEntries(updatedOrder.items.map((item) => [
      item.id,
      { quantity: String(item.quantity), specialRequest: item.specialRequest ?? "" },
    ])));
    return updatedOrder;
  }, []);

  const refreshAssignments = async () => {
    if (operationLock.current) return;
    operationLock.current = true;
    setRefreshingTables(true);
    setError(null);
    setNotice(null);
    try {
      const assignedTables = await getWaiterTables();
      setTables(assignedTables);
      setAssignmentsUnavailable(false);
      void refreshOrderHistory(false);

      const selectedTableIsAssigned = selectedTableId !== null &&
        assignedTables.some((table) => table.id === selectedTableId);
      if (selectedTableId !== null && !selectedTableIsAssigned) {
        setSelectedTableId(null);
        setOrder(null);
        setItemDrafts({});
        setNotice("Your table assignment changed. The table and its order are no longer available.");
        return;
      }

      if (order && selectedTableId !== null) {
        try {
          await refreshOrder(order.id, selectedTableId);
        } catch (caughtError) {
          setOrder(null);
          setItemDrafts({});
          if (caughtError instanceof ApiError &&
              [401, 403, 404].includes(caughtError.status)) {
            setNotice("The selected order is no longer available. Refresh assignments before continuing.");
          } else {
            setError(`Could not refresh the selected order: ${getErrorMessage(caughtError, "Please try again.")}`);
          }
        }
      } else if (assignedTables.length === 0) {
        setNotice("No active tables are assigned to your waiter account.");
      }
    } catch (caughtError) {
      setTables([]);
      setAssignmentsUnavailable(true);
      setSelectedTableId(null);
      setOrder(null);
      setItemDrafts({});
      setError(`Could not refresh assigned tables: ${getErrorMessage(caughtError, "Please try again.")}`);
    } finally {
      operationLock.current = false;
      setRefreshingTables(false);
    }
  };

  const openTable = async (table: WaiterTableResponse, seatNumber?: number) => {
    if (operationLock.current) return;
    if (!tables.some((assignedTable) => assignedTable.id === table.id)) return;
    operationLock.current = true;
    setSelectedTableId(table.id);
    setOrder(null);
    setItemDrafts({});
    setOpening(true);
    setError(null);
    setNotice(null);
    try {
      const opened = await openOrCreateOrder(table.id, seatNumber);
      await refreshOrder(opened.orderId, table.id);
      void refreshOrderHistory(false);
    } catch (caughtError) {
      if (caughtError instanceof ApiError &&
          [401, 403, 404].includes(caughtError.status)) {
        setTables([]);
        setAssignmentsUnavailable(true);
        setSelectedTableId(null);
        setOrder(null);
        setItemDrafts({});
      }
      setError(getErrorMessage(caughtError, "Could not open this table's order."));
    } finally {
      operationLock.current = false;
      setOpening(false);
    }
  };

  const openHistoryOrder = async (entry: WaiterOrderHistoryEntry) => {
    if (operationLock.current) return;
    if (!tables.some((table) => table.id === entry.diningTableId)) {
      setHistoryError("This order's table is no longer assigned to you. Refresh the history to update the list.");
      return;
    }
    operationLock.current = true;
    setSelectedTableId(entry.diningTableId);
    setOrder(null);
    setItemDrafts({});
    setOpening(true);
    setError(null);
    setNotice(null);
    try {
      await refreshOrder(entry.id, entry.diningTableId);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not open this order from history."));
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
        await refreshOrder(order.id, order.diningTableId);
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
      await refreshOrder(order.id, order.diningTableId);
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
        await refreshOrder(order.id, order.diningTableId);
      } catch {
        setNotice("The saved order could not be reloaded. Retry the refresh before editing again.");
      }
      operationLock.current = false;
      setBusyItemId(null);
      return;
    }
    try {
      await refreshOrder(order.id, order.diningTableId);
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
        await refreshOrder(order.id, order.diningTableId);
      } catch {
        setNotice("The saved order could not be reloaded. Retry the refresh before editing again.");
      }
      operationLock.current = false;
      setBusyItemId(null);
      return;
    }
    try {
      await refreshOrder(order.id, order.diningTableId);
    } catch (caughtError) {
      setNotice(`Dish removed, but the order could not be reloaded: ${getErrorMessage(caughtError, "Please refresh the order.")}`);
    } finally {
      operationLock.current = false;
      setBusyItemId(null);
    }
  };

  const submitOrder = async () => {
    if (!order || operationLock.current) return;
    const changedItems: Array<{ item: OrderItemDetailsResponse; draft: ItemDraft }> = [];
    for (const item of order.items) {
      const draft = itemDrafts[item.id];
      if (!draft || (draft.quantity === String(item.quantity) &&
          draft.specialRequest === (item.specialRequest ?? ""))) {
        continue;
      }

      const parsedQuantity = Number(draft.quantity);
      if (!Number.isSafeInteger(parsedQuantity) || parsedQuantity <= 0) {
        setError("Quantity must be a positive whole number.");
        return;
      }
      changedItems.push({ item, draft });
    }

    operationLock.current = true;
    setSending(true);
    setError(null);
    setNotice(null);
    try {
      for (const changedItem of changedItems) {
        await updateOrderItem(
          order.id,
          changedItem.item.id,
          Number(changedItem.draft.quantity),
          changedItem.draft.specialRequest,
        );
      }

      const reviewedOrder = changedItems.length > 0
        ? await refreshOrder(order.id, order.diningTableId)
        : order;
      const result = await sendOrder(reviewedOrder.id);
      if (result.kitchenOrder.diningTableId !== reviewedOrder.diningTableId) {
        setSelectedTableId(null);
        setOrder(null);
        setItemDrafts({});
        throw new Error("The returned order does not belong to the selected table.");
      }
      setOrder(result.kitchenOrder);
      setItemDrafts(Object.fromEntries(result.kitchenOrder.items.map((item) => [
        item.id,
        { quantity: String(item.quantity), specialRequest: item.specialRequest ?? "" },
      ])));
      setNotice(result.alreadySent
        ? "This order was already sent; the kitchen has one copy."
        : "Order sent to the kitchen.");
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not send this order."));
      try {
        const refreshedOrder = await refreshOrder(order.id, order.diningTableId);
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
      await refreshOrder(order.id, order.diningTableId);
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
          <WorkspaceSessionActions />
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
                <button
                  type="button"
                  className="secondary-button"
                  disabled={refreshingTables || opening || savingItem || sending || busyItemId !== null}
                  onClick={() => void refreshAssignments()}
                >
                  {refreshingTables ? "Refreshing..." : "Refresh assignments"}
                </button>
              </div>
              {refreshingTables ? (
                <p className="staff-help" role="status">Refreshing assigned tables...</p>
              ) : tables.length === 0 ? (
                <p className="staff-help">
                  {assignmentsUnavailable
                    ? "Assigned tables are unavailable until they can be refreshed."
                    : "No active tables are assigned to your waiter account."}
                </p>
              ) : (
                <div className="order-table-list">
                  {tables.map((table) => (
                    <button
                      type="button"
                      key={table.id}
                      className={`order-table-button${selectedTableId === table.id ? " selected" : ""}`}
                      disabled={opening || refreshingTables}
                      onClick={() => void openTable(table)}
                    >
                      <strong>Table {table.tableNumber}</strong>
                      <span>{table.seatCount} seats</span>
                    </button>
                  ))}
                </div>
              )}
            </section>

            <section className="staff-card" aria-labelledby="waiter-order-history-title">
              <div className="staff-card-heading">
                <div>
                  <p className="eyebrow">ORDER HISTORY</p>
                  <h2 id="waiter-order-history-title">Orders on assigned tables</h2>
                </div>
                <button
                  type="button"
                  className="secondary-button"
                  disabled={historyLoading || opening}
                  onClick={() => void refreshOrderHistory()}
                >
                  {historyLoading ? "Refreshing..." : "Refresh history"}
                </button>
              </div>
              {historyError && (
                <p className="staff-notice error" role="alert">
                  {historyError}
                  <button type="button" className="staff-text-button" onClick={() => void refreshOrderHistory()}>
                    Try again
                  </button>
                </p>
              )}
              {historyLoading ? (
                <p className="staff-empty" role="status">Loading order history...</p>
              ) : orderHistory.length === 0 ? (
                <p className="staff-empty" role="status">No order history is available for your currently assigned tables.</p>
              ) : (
                <div className="waiter-order-history">
                  {orderHistory.map((entry) => {
                    const table = tables.find((candidate) => candidate.id === entry.diningTableId);
                    return (
                      <button
                        type="button"
                        key={entry.id}
                        className={`waiter-order-history-entry${order?.id === entry.id ? " selected" : ""}`}
                        aria-pressed={order?.id === entry.id}
                        disabled={opening || refreshingTables}
                        onClick={() => void openHistoryOrder(entry)}
                      >
                        <strong>Order #{entry.id}</strong>
                        <span>{table ? `Table ${table.tableNumber}` : "Assigned table"}</span>
                        <time dateTime={entry.createdAtUtc}>
                          {new Date(entry.createdAtUtc).toLocaleString()}
                        </time>
                        <span className="waiter-order-history-action">
                          {order?.id === entry.id ? "Viewing" : "View order"}
                        </span>
                      </button>
                    );
                  })}
                </div>
              )}
            </section>

            {order && activeTable && !refreshingTables && (
              <section className="staff-card order-composer" aria-labelledby="order-heading">
                <div className="staff-card-heading">
                  <div>
                    <p className="eyebrow">
                      TABLE {activeTable.tableNumber}{order.seatNumber ? ` / SEAT ${order.seatNumber}` : ""} / ORDER #{order.id}
                    </p>
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
                        onClick={() => void reloadOrder()}
                      >
                        {opening ? "Opening..." : "Reopen order"}
                      </button>
                    )}
                  </div>
                </div>

                {orderIsActive && (
                  <div className="order-review-heading">
                    <h3>{order.status === "Sent" ? "Continue this order" : "Review order before sending"}</h3>
                    <p>{order.status === "Sent"
                      ? "Submitted dishes can be edited here, but the current API may reject those changes."
                      : "Review each dish, quantity, and special request before sending the order."}</p>
                  </div>
                )}

                {orderIsActive && order.items.some((item) => item.kitchenSubmissionId !== null) && (
                  <p className="staff-notice warning" role="status">
                    The server rejects edits and removals for submitted dishes; those actions may fail with a conflict and won't update the kitchen ticket.
                  </p>
                )}

                {order.items.length === 0 ? (
                  <p className="staff-help">No dishes added yet. Add at least one dish before sending.</p>
                ) : (
                  <div className="order-items">
                    {order.items.map((item) => {
                      const draft = itemDrafts[item.id] ?? {
                        quantity: String(item.quantity),
                        specialRequest: item.specialRequest ?? "",
                      };
                      const editable = orderIsActive;
                      return (
                        <form
                          className="order-item"
                          key={item.id}
                          onSubmit={(event) => {
                            event.preventDefault();
                            if (editable) void saveItem(item, draft);
                          }}
                        >
                          <div className="order-item-title">
                            <strong>{item.dishName}</strong>
                            <span>{formatPrice(item.unitPrice)} each</span>
                            {item.kitchenSubmissionId !== null && (
                              <em className="order-submitted-tag">Submitted</em>
                            )}
                          </div>
                          <label>
                            Quantity
                            <input
                              type="number"
                              min="1"
                              step="1"
                              required
                              disabled={!editable || busyItemId === item.id || savingItem || sending}
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
                              disabled={!editable || busyItemId === item.id || savingItem || sending}
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
                          {editable && (
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

                {orderIsActive && (
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
                      <span>{order.status === "Sent" ? "Updated subtotal" : "Subtotal"}</span>
                      <strong>{formatPrice(total)}</strong>
                    </div>
                    <p className="order-fee-note">Subtotal only; no additional service fee is applied.</p>
                    <button
                      type="button"
                      className="primary-button order-send-button"
                      disabled={pendingItems.length === 0 || sending || savingItem || busyItemId !== null}
                      onClick={() => void submitOrder()}
                    >
                      {sending ? "Sending..." : order.status === "Sent" ? "Send new items to kitchen" : "Send order to kitchen"}
                    </button>
                  </>
                )}
                {order.status === "Sent" && pendingItems.length === 0 && (
                  <div className="order-total">
                    <span>Kitchen subtotal</span>
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
