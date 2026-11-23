import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import {
  ApiError,
  addOrderItem,
  cancelWaiterOrder,
  getCurrentAccount,
  getMenuDishes,
  getOrderDetails,
  getWaiterOrderHistory,
  getWaiterNotifications,
  markWaiterItemServed,
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
  WaiterNotificationResponse,
  WaiterTableResponse,
} from "./services/api";
import { formatPrice } from "./services/currency";
import { useKitchenUpdates } from "./services/useKitchenUpdates";
import { useMenuUpdates } from "./services/useMenuUpdates";
import WorkspaceSessionActions from "./WorkspaceSessionActions";
import WaiterCancelDialog from "./WaiterCancelDialog";
import "./waiter-order-table-actions.css";

type ItemDraft = {
  quantity: string;
  specialRequest: string;
};

function getErrorMessage(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

function isPositiveWholeNumber(value: string): boolean {
  const parsed = Number(value);
  return value.trim().length > 0 && Number.isSafeInteger(parsed) && parsed > 0;
}

const preparationStatusRank = {
  New: 0,
  Preparing: 1,
  Ready: 2,
} as const;

function WaiterOrderPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [tables, setTables] = useState<WaiterTableResponse[]>([]);
  const [dishes, setDishes] = useState<MenuDishResponse[]>([]);
  const [order, setOrder] = useState<OrderDetailsResponse | null>(null);
  const [orderHistory, setOrderHistory] = useState<WaiterOrderHistoryEntry[]>([]);
  const [notifications, setNotifications] = useState<WaiterNotificationResponse[]>([]);
  const [notificationLoading, setNotificationLoading] = useState(false);
  const [notificationError, setNotificationError] = useState<string | null>(null);
  const [servingItemId, setServingItemId] = useState<number | null>(null);
  const [readyToast, setReadyToast] = useState<WaiterNotificationResponse | null>(null);
  const seenNotificationIds = useRef<Set<number>>(new Set());
  const notificationFeedInitialized = useRef(false);
  const notificationRequestVersion = useRef(0);
  const servingItemLock = useRef(false);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [workspaceLoadError, setWorkspaceLoadError] = useState<string | null>(null);
  const [selectedTableId, setSelectedTableId] = useState<number | null>(null);
  const [selectedDishId, setSelectedDishId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [specialRequest, setSpecialRequest] = useState("");
  const [itemDrafts, setItemDrafts] = useState<Record<number, ItemDraft>>({});
  const [itemValidationErrors, setItemValidationErrors] = useState<Record<number, string>>({});
  const [loading, setLoading] = useState(true);
  const [refreshingTables, setRefreshingTables] = useState(false);
  const [assignmentsUnavailable, setAssignmentsUnavailable] = useState(false);
  const [opening, setOpening] = useState(false);
  const [savingItem, setSavingItem] = useState(false);
  const [busyItemId, setBusyItemId] = useState<number | null>(null);
  const [sending, setSending] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState("");
  const [cancelConfirmed, setCancelConfirmed] = useState(false);
  const [cancelValidationError, setCancelValidationError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [statusRefreshError, setStatusRefreshError] = useState<string | null>(null);
  const operationLock = useRef(false);
  const statusRefreshInFlight = useRef(false);
  const addDishFormRef = useRef<HTMLFormElement>(null);
  const orderIsActive = order !== null && (order.status === "Open" || order.status === "Sent");
  const pendingItems = order?.items.filter((item) => item.kitchenSubmissionId === null) ?? [];
  const hasSubmittedItems = order?.items.some((item) =>
    item.kitchenSubmissionId !== null || item.preparationStatus !== "New") ?? false;
  const canCancelOrder = order?.status === "Open" && order.sentAtUtc === null && !hasSubmittedItems;
  const total = useMemo(
    () => order?.items.reduce((sum, item) => {
      const draftQuantity = itemDrafts[item.id]?.quantity;
      const currentQuantity = draftQuantity && isPositiveWholeNumber(draftQuantity)
        ? Number(draftQuantity)
        : item.quantity;
      return sum + item.unitPrice * currentQuantity;
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
          setAssignmentsUnavailable(true);
          const message = getErrorMessage(caughtError, "Could not load the waiter workspace.");
          if (caughtError instanceof ApiError &&
              [401, 403].includes(caughtError.status)) {
            setAuthorized(false);
            setError(message);
          } else {
            setAuthorized(null);
            setWorkspaceLoadError(message);
            setError(null);
          }
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
    setStatusRefreshError(null);
    setItemValidationErrors({});
    setItemDrafts(Object.fromEntries(updatedOrder.items.map((item) => [
      item.id,
      { quantity: String(item.quantity), specialRequest: item.specialRequest ?? "" },
    ])));
    return updatedOrder;
  }, []);

  const refreshSubmittedStatuses = useCallback(async (
    orderId: number,
    expectedTableId: number,
  ) => {
    if (statusRefreshInFlight.current || operationLock.current) return;
    statusRefreshInFlight.current = true;
    try {
      const latestOrder = await getOrderDetails(orderId);
      if (latestOrder.id !== orderId || latestOrder.diningTableId !== expectedTableId) {
        throw new Error("The refreshed status does not match the selected order.");
      }

      const latestItems = new Map(latestOrder.items.map((item) => [item.id, item]));
      setOrder((current) => {
        if (!current || current.id !== orderId ||
            current.diningTableId !== expectedTableId) {
          return current;
        }

        return {
          ...current,
          items: current.items.map((item) => {
            const latestItem = latestItems.get(item.id);
            if (!latestItem) return item;

            const latestStatusIsCurrentOrNewer =
              preparationStatusRank[latestItem.preparationStatus] >=
              preparationStatusRank[item.preparationStatus];

            return {
              ...item,
              kitchenSubmissionId: item.kitchenSubmissionId ?? latestItem.kitchenSubmissionId,
              preparationStatus: latestStatusIsCurrentOrNewer
                ? latestItem.preparationStatus
                : item.preparationStatus,
              preparationStartedAtUtc: latestStatusIsCurrentOrNewer
                ? latestItem.preparationStartedAtUtc ?? item.preparationStartedAtUtc
                : item.preparationStartedAtUtc,
              readyAtUtc: latestStatusIsCurrentOrNewer
                ? latestItem.readyAtUtc ?? item.readyAtUtc
                : item.readyAtUtc,
            };
          }),
        };
      });
      setStatusRefreshError(null);
    } catch (caughtError) {
      setStatusRefreshError(
        getErrorMessage(caughtError, "Could not refresh kitchen preparation status."),
      );
    } finally {
      statusRefreshInFlight.current = false;
    }
  }, []);

  const refreshNotifications = useCallback(async (showLoading = false) => {
    const requestVersion = ++notificationRequestVersion.current;
    if (showLoading) setNotificationLoading(true);
    setNotificationError(null);
    try {
      const current = await getWaiterNotifications();
      if (requestVersion !== notificationRequestVersion.current) return;

      const newestUnseen = notificationFeedInitialized.current
        ? current.find((notification) =>
          notification.serviceStatus !== "Served" &&
          !seenNotificationIds.current.has(notification.id))
        : undefined;
      current.forEach((notification) => seenNotificationIds.current.add(notification.id));
      notificationFeedInitialized.current = true;
      setNotifications(current);
      setReadyToast((existing) => {
        if (existing && current.some((notification) =>
          notification.orderItemId === existing.orderItemId &&
          notification.serviceStatus === "Served")) {
          return null;
        }
        return newestUnseen ?? existing;
      });
    } catch (caughtError) {
      if (requestVersion === notificationRequestVersion.current) {
        setNotificationError(getErrorMessage(caughtError, "Could not load ready-dish notifications."));
      }
    } finally {
      if (requestVersion === notificationRequestVersion.current) {
        setNotificationLoading(false);
      }
    }
  }, []);

  const handleMarkServed = async (notification: WaiterNotificationResponse) => {
    if (servingItemLock.current || notification.serviceStatus === "Served") return;
    servingItemLock.current = true;
    setServingItemId(notification.orderItemId);
    setNotificationError(null);
    try {
      const result = await markWaiterItemServed(notification.orderItemId);
      if (result.id !== notification.orderItemId ||
          result.orderId !== notification.orderId ||
          result.serviceStatus !== "Served") {
        throw new Error("The API response did not confirm this dish as served.");
      }
      setNotifications((current) => current.map((entry) => entry.orderItemId === notification.orderItemId
        ? { ...entry, serviceStatus: "Served" }
        : entry));
      setReadyToast((current) => current?.orderItemId === notification.orderItemId ? null : current);
      setNotice(`${notification.dishName} marked as served.`);
      await refreshNotifications();
    } catch (caughtError) {
      setNotificationError(getErrorMessage(caughtError, "Could not mark the dish as served."));
    } finally {
      servingItemLock.current = false;
      setServingItemId(null);
    }
  };


  useEffect(() => {
    if (!authorized) return;
    void refreshNotifications(true);
    const timer = window.setInterval(() => {
      if (document.visibilityState === "visible") void refreshNotifications();
    }, 5000);
    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") void refreshNotifications();
    };
    document.addEventListener("visibilitychange", refreshWhenVisible);
    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", refreshWhenVisible);
    };
  }, [authorized, refreshNotifications]);

  useKitchenUpdates(() => {
    if (authorized) void refreshNotifications();
    if (authorized && order && hasSubmittedItems) {
      void refreshSubmittedStatuses(order.id, order.diningTableId);
    }
  });

  useEffect(() => {
    if (!authorized || !order || !hasSubmittedItems) return;

    const refreshWhenVisible = () => {
      if (document.visibilityState === "visible") {
        void refreshSubmittedStatuses(order.id, order.diningTableId);
      }
    };
    const timer = window.setInterval(refreshWhenVisible, 5000);
    document.addEventListener("visibilitychange", refreshWhenVisible);

    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", refreshWhenVisible);
    };
  }, [authorized, hasSubmittedItems, order?.diningTableId, order?.id, refreshSubmittedStatuses]);

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

  // Clicking a table must NOT create a new order: first select a seat.
  const selectTable = (table: WaiterTableResponse) => {
    if (operationLock.current || !tables.some((assigned) => assigned.id === table.id)) return;
    setSelectedTableId(table.id);
    setOrder(null);
    setItemDrafts({});
    setError(null);
    setNotice(null);
  };

  const createNewOrder = async (table: WaiterTableResponse, seatNumber: number) => {
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
      // Refresh first so an already-active seat is never offered as a new order.
      const latestHistory = await getWaiterOrderHistory();
      const sortedHistory = latestHistory.sort(
        (first, second) => Date.parse(second.createdAtUtc) - Date.parse(first.createdAtUtc),
      );
      setOrderHistory(sortedHistory);

      const activeOrder = sortedHistory.find((entry) =>
        entry.diningTableId === table.id &&
        entry.seatNumber === seatNumber &&
        (entry.status === "Open" || entry.status === "Sent"),
      );

      if (activeOrder) {
        setNotice(
          `A new order was not created. Seat ${seatNumber} already has active order #${activeOrder.id}. ` +
          "Cancel the unsent order or choose another available seat.",
        );
        return;
      }

      // The current backend route is GetOrCreateOpen. Refuse its reuse result
      // rather than ever opening an existing order from the New order action.
      const created = await openOrCreateOrder(table.id, seatNumber);
      if (created.reused) {
        const refreshedHistory = await getWaiterOrderHistory().catch(() => latestHistory);
        setOrderHistory(refreshedHistory.sort(
          (first, second) => Date.parse(second.createdAtUtc) - Date.parse(first.createdAtUtc),
        ));
        setNotice(
          `A new order was not created because an active order (#${created.orderId}) now exists ` +
          `for Table ${table.tableNumber}, Seat ${seatNumber}. No existing order was opened.`,
        );
        return;
      }

      const createdOrder = await refreshOrder(created.orderId, table.id);
      if (createdOrder.id !== created.orderId || createdOrder.seatNumber !== seatNumber) {
        throw new Error("The server response did not match the newly created table seat order.");
      }

      setNotice(
        `New order #${created.orderId} created for Table ${table.tableNumber}, Seat ${seatNumber}.`,
      );
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
      setError(getErrorMessage(caughtError, "Could not create a new order for this table seat."));
      void refreshOrderHistory(false);
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

  const focusAddDish = (dishId?: number) => {
    if (dishId !== undefined) {
      if (!availableDishes.some((dish) => dish.id === dishId)) {
        setError("This dish is no longer available. Choose another dish from the menu.");
        return;
      }
      setSelectedDishId(String(dishId));
    }
    if (dishId === undefined && !availableDishes.some((dish) => String(dish.id) === selectedDishId)) {
      setSelectedDishId(String(availableDishes[0]?.id ?? ""));
    }
    setQuantity("1");
    setSpecialRequest("");
    setError(null);
    addDishFormRef.current?.scrollIntoView({ behavior: "smooth", block: "center" });
    addDishFormRef.current?.querySelector("select")?.focus({ preventScroll: true });
  };

  const addItem = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order || !orderIsActive || operationLock.current) return;
    const dishId = Number(selectedDishId);
    const parsedQuantity = Number(quantity);
    if (!Number.isSafeInteger(dishId) || dishId <= 0 ||
        !isPositiveWholeNumber(quantity)) {
      setError("Choose an available dish and enter a quantity that is a whole number of at least 1.");
      return;
    }

    operationLock.current = true;
    setSavingItem(true);
    setError(null);
    setNotice(null);
    try {
      await addOrderItem(
        order.id,
        dishId,
        parsedQuantity,
        specialRequest,
        "Added from the waiter order screen.",
      );
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
    if (!orderIsActive || item.kitchenSubmissionId !== null) {
      setError("Submitted dishes cannot be edited. Use Add more to request an additional quantity.");
      return;
    }
    if (!isPositiveWholeNumber(draft.quantity)) {
      setItemValidationErrors((current) => ({
        ...current,
        [item.id]: "Enter a whole number of at least 1.",
      }));
      setError(null);
      return;
    }
    const parsedQuantity = Number(draft.quantity);

    operationLock.current = true;
    setBusyItemId(item.id);
    setError(null);
    setNotice(null);
    try {
      await updateOrderItem(
        order.id,
        item.id,
        parsedQuantity,
        draft.specialRequest,
        "Updated from the waiter order screen.",
      );
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
      setItemValidationErrors((current) => {
        const next = { ...current };
        delete next[item.id];
        return next;
      });
    } catch (caughtError) {
      setNotice(`Dish updated, but the order could not be reloaded: ${getErrorMessage(caughtError, "Please refresh the order.")}`);
    } finally {
      operationLock.current = false;
      setBusyItemId(null);
    }
  };

  const deleteItem = async (itemId: number) => {
    if (!order || operationLock.current) return;
    const item = order.items.find((current) => current.id === itemId);
    if (!orderIsActive || !item || item.kitchenSubmissionId !== null) {
      setError("Submitted dishes cannot be removed from a kitchen ticket.");
      return;
    }
    operationLock.current = true;
    setBusyItemId(itemId);
    setError(null);
    setNotice(null);
    try {
      await removeOrderItem(order.id, itemId, "Removed from the waiter order screen.");
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

  const openCancelDialog = () => {
    if (!order || !canCancelOrder || operationLock.current) return;
    setCancelReason("");
    setCancelConfirmed(false);
    setCancelValidationError(null);
    setCancelDialogOpen(true);
  };

  const closeCancelDialog = () => {
    if (cancelling) return;
    setCancelDialogOpen(false);
    setCancelValidationError(null);
  };

  const submitCancellation = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order || !canCancelOrder || operationLock.current) {
      setCancelValidationError(
        hasSubmittedItems
          ? "This order already contains kitchen-submitted items and cannot be cancelled from the waiter screen."
          : "This order is no longer active and cannot be cancelled.",
      );
      return;
    }

    const reason = cancelReason.trim();
    if (!reason) {
      setCancelValidationError("Enter a reason for cancelling this order.");
      return;
    }
    if (reason.length > 500) {
      setCancelValidationError("The cancellation reason must be 500 characters or fewer.");
      return;
    }
    if (!cancelConfirmed) {
      setCancelValidationError("Confirm that you want to cancel this order before continuing.");
      return;
    }

    const orderId = order.id;
    const tableId = order.diningTableId;
    operationLock.current = true;
    setCancelling(true);
    setCancelValidationError(null);
    setError(null);
    setNotice(null);

    try {
      const result = await cancelWaiterOrder(orderId, true, reason);
      if (result.orderId !== orderId || result.status !== "Cancelled") {
        throw new Error("The API did not confirm cancellation of this order.");
      }

      setOrder((current) => current?.id === orderId
        ? { ...current, status: "Closed" }
        : current);
      setOrderHistory((current) => current.map((entry) => entry.id === orderId
        ? { ...entry, status: "Closed" }
        : entry));
      setCancelDialogOpen(false);
      setCancelReason("");
      setCancelConfirmed(false);
      setNotice(`Order #${orderId} was cancelled.`);
      void refreshOrderHistory(false);

      try {
        const refreshed = await refreshOrder(orderId, tableId);
        if (refreshed.status !== "Closed") {
          throw new Error("The refreshed order did not show the cancelled status.");
        }
      } catch {
        setNotice(`Order #${orderId} was cancelled, but the latest order details could not be reloaded. Refresh the order before continuing.`);
      }
    } catch (caughtError) {
      const message = getErrorMessage(caughtError, "Could not cancel this order.");
      setCancelValidationError(message);

      // A lost response must not leave the screen claiming that a successful
      // cancellation failed when the server actually completed it.
      try {
        const latest = await refreshOrder(orderId, tableId);
        if (latest.status === "Closed") {
          setOrderHistory((current) => current.map((entry) => entry.id === orderId
            ? { ...entry, status: "Closed" }
            : entry));
          setCancelDialogOpen(false);
          setCancelReason("");
          setCancelConfirmed(false);
          setCancelValidationError(null);
          setNotice(`Order #${orderId} was cancelled and the updated status was verified.`);
          void refreshOrderHistory(false);
        }
      } catch {
        // Keep the API error visible in the confirmation dialog.
      }
    } finally {
      operationLock.current = false;
      setCancelling(false);
    }
  };

  const submitOrder = async () => {
    if (!order || !orderIsActive || operationLock.current || pendingItems.length === 0) return;
    const pendingIds = new Set(pendingItems.map((item) => item.id));
    const changedItems: Array<{ item: OrderItemDetailsResponse; draft: ItemDraft }> = [];
    for (const item of pendingItems) {
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
          "Updated from the waiter order screen before sending.",
        );
      }

      const reviewedOrder = changedItems.length > 0
        ? await refreshOrder(order.id, order.diningTableId)
        : order;
      const result = await sendOrder(reviewedOrder.id);
      if (result.kitchenOrder.diningTableId !== reviewedOrder.diningTableId ||
          result.orderId !== reviewedOrder.id) {
        setSelectedTableId(null);
        setOrder(null);
        setItemDrafts({});
        throw new Error("The returned kitchen submission does not match this order.");
      }
      // kitchenOrder contains only submitted items; fetch the full waiter order.
      const refreshed = await refreshOrder(order.id, order.diningTableId);
      const submittedIds = new Set(refreshed.items
        .filter((item) => item.kitchenSubmissionId !== null)
        .map((item) => item.id));
      if (![...pendingIds].every((id) => submittedIds.has(id))) {
        throw new Error("Not every pending dish was confirmed as submitted. Reload the order before retrying.");
      }
      void refreshOrderHistory(false);
      setNotice(result.alreadySent
        ? "These dishes were already sent; no duplicate ticket was created."
        : `New dishes sent to the kitchen (submission #${result.submissionId ?? "confirmed"}). You can add more to this order.`);
    } catch (caughtError) {
      setError(getErrorMessage(caughtError, "Could not send the new items."));
      try {
        const refreshed = await refreshOrder(order.id, order.diningTableId);
        const submittedIds = new Set(refreshed.items
          .filter((item) => item.kitchenSubmissionId !== null)
          .map((item) => item.id));
        // An existing Sent status is NOT evidence that this submission succeeded.
        if (pendingIds.size > 0 && [...pendingIds].every((id) => submittedIds.has(id))) {
          setError(null);
          setNotice("The new dishes reached the kitchen. The order has been refreshed.");
          void refreshOrderHistory(false);
        }
      } catch {
        setNotice("Could not verify the new kitchen submission. Reload this order before retrying.");
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
        {authorized && (
          <section className="waiter-ready-inbox staff-card" aria-labelledby="waiter-ready-title">
            <div className="staff-card-heading">
              <div><p className="eyebrow">KITCHEN UPDATES</p><h2 id="waiter-ready-title">Ready dishes</h2></div>
              <button type="button" className="secondary-button" disabled={notificationLoading} onClick={() => void refreshNotifications(true)}>
                {notificationLoading ? "Refreshing..." : "Refresh notifications"}
              </button>
            </div>
            <p className="staff-help">Newly prepared dishes for your assigned tables. Mark each dish served after delivery.</p>
            {notificationError && <p className="staff-notice error" role="alert">{notificationError}<button type="button" className="staff-text-button" onClick={() => void refreshNotifications(true)}>Try again</button></p>}
            {notificationLoading && notifications.length === 0 ? (
              <div className="waiter-notification-loading" role="status" aria-busy="true"><span className="admin-spinner" /> Loading ready dishes...</div>
            ) : notifications.length === 0 ? (
              <div className="waiter-notification-empty" role="status"><strong>All clear</strong><span>No ready dishes to deliver right now.</span></div>
            ) : (
              <div className="waiter-notification-list">
                {notifications.map((notification) => (
                  <article className={`waiter-notification${notification.serviceStatus === "Served" ? " served" : ""}`} key={notification.id}>
                    <div className="waiter-notification-main">
                      <span className={`waiter-notification-status ${notification.serviceStatus === "Served" ? "served" : "ready"}`}>{notification.serviceStatus === "Served" ? "Served" : "Ready to deliver"}</span>
                      <strong>{notification.dishName}{notification.quantity > 1 ? ` × ${notification.quantity}` : ""}</strong>
                      <span className="waiter-notification-table">Table {notification.tableNumber} · Order #{notification.orderId}</span>
                      <time dateTime={notification.createdAtUtc}>{new Date(notification.createdAtUtc).toLocaleTimeString()}</time>
                    </div>
                    <button type="button" className="kitchen-status-button ready" disabled={servingItemId !== null || notification.serviceStatus === "Served"} onClick={() => void handleMarkServed(notification)}>
                      {servingItemId === notification.orderItemId ? "Saving..." : notification.serviceStatus === "Served" ? "Served ✓" : "Mark served"}
                    </button>
                  </article>
                ))}
              </div>
            )}
          </section>
        )}
        {readyToast && authorized && (
          <aside className="waiter-ready-toast" role="status" aria-live="polite">
            <div><span className="waiter-toast-kicker">Dish ready</span><strong>{readyToast.dishName}</strong><span>Table {readyToast.tableNumber}{readyToast.quantity > 1 ? ` · × ${readyToast.quantity}` : ""}</span></div>
            <button type="button" aria-label="Dismiss ready dish notification" onClick={() => setReadyToast(null)}>×</button>
          </aside>
        )}

        {loading ? (
          <div className="admin-loading" role="status" aria-busy="true">
            <span className="admin-spinner" /> Loading waiter workspace...
          </div>
        ) : authorized === null ? (
          <section className="staff-card" role="alert">
            <h2>Could not load waiter workspace</h2>
            <p className="staff-help">
              {workspaceLoadError ?? "The waiter workspace could not be loaded. Check the connection and try again."}
            </p>
            <button type="button" className="secondary-button" onClick={() => window.location.reload()}>
              Try again
            </button>
          </section>
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
                    <article
                      key={table.id}
                      className={`order-table-card${selectedTableId === table.id ? " selected" : ""}`}
                    >
                      <div className="order-table-card-details">
                        <strong>Table {table.tableNumber}</strong>
                        <span>{table.seatCount} seats</span>
                      </div>
                      <button
                        type="button"
                        className="order-table-new-order-button"
                        disabled={opening || refreshingTables || savingItem || sending || busyItemId !== null}
                        aria-label={`Start a new order at Table ${table.tableNumber}`}
                        onClick={() => selectTable(table)}
                      >
                        <span aria-hidden="true">+</span>
                        New order
                      </button>
                    </article>
                  ))}
                </div>
              )}
              {selectedTableId !== null && activeTable && (
                <div className="order-seat-selector">
                  <h3>Create a new order at Table {activeTable.tableNumber}</h3>
                  <p className="staff-help">
                    Select an available seat. This action creates a brand-new order with a new ID; seats with an active order cannot be reopened here.
                    After an unsent order is cancelled, its seat becomes available for another new order.
                  </p>
                  <div className="order-seat-list">
                    {Array.from({ length: activeTable.seatCount }, (_, index) => index + 1).map((seat) => {
                      const existing = orderHistory.find((entry) =>
                        entry.diningTableId === activeTable.id &&
                        entry.seatNumber === seat &&
                        (entry.status === "Open" || entry.status === "Sent"));
                      return (
                        <button
                          type="button"
                          key={seat}
                          className={`order-seat-button${order?.seatNumber === seat ? " selected" : ""}${existing ? " occupied" : " available"}`}
                          aria-pressed={order?.seatNumber === seat}
                          aria-label={existing
                            ? `Seat ${seat} unavailable because order ${existing.id} is ${existing.status}`
                            : `Create a new order for seat ${seat}`}
                          disabled={Boolean(existing) || opening || refreshingTables || savingItem || sending || busyItemId !== null}
                          onClick={() => void createNewOrder(activeTable, seat)}
                        >
                          <strong>Seat {seat}</strong>
                          <span>{existing ? `Active order #${existing.id} · ${existing.status}` : "Available"}</span>
                          <span className="order-seat-button-action">
                            {existing ? "Seat unavailable" : "Create new order"}
                          </span>
                        </button>
                      );
                    })}
                  </div>
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
                        <span>
                          {table ? `Table ${table.tableNumber}` : "Assigned table"}
                          {entry.seatNumber != null ? ` · Seat ${entry.seatNumber}` : " · No seat assigned"}
                        </span>
                        <time dateTime={entry.createdAtUtc}>
                          {new Date(entry.createdAtUtc).toLocaleString()}
                        </time>
                        <span className="waiter-order-history-action">
                          {entry.status} · {order?.id === entry.id ? "Viewing" : "View order"}
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
                    <h2 id="order-heading">{order.status === "Sent" ? "Sent to kitchen · Still active" : order.status === "Closed" ? "Order closed" : "Order draft"}</h2>
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
                    {orderIsActive && (
                      <button
                        type="button"
                        className="secondary-button"
                        disabled={savingItem || sending || busyItemId !== null || availableDishes.length === 0}
                        onClick={() => focusAddDish()}
                      >
                        Add more dishes
                      </button>
                    )}
                    {canCancelOrder && (
                      <button
                        type="button"
                        className="order-cancel-button"
                        disabled={opening || savingItem || sending || cancelling || busyItemId !== null}
                        aria-describedby={hasSubmittedItems ? "waiter-cancel-unavailable" : undefined}
                        title={hasSubmittedItems
                          ? "View why this order cannot be cancelled"
                          : "Cancel this unpaid order"}
                        onClick={openCancelDialog}
                      >
                        Cancel order
                      </button>
                    )}
                  </div>
                </div>

                {orderIsActive && hasSubmittedItems && (
                  <p className="staff-notice warning" id="waiter-cancel-unavailable" role="status">
                    This order contains items already sent to the kitchen. The current waiter cancellation endpoint
                    cannot withdraw submitted kitchen items, so cancellation is unavailable.
                  </p>
                )}

                {statusRefreshError && (
                  <p className="staff-notice error kitchen-status-refresh-error" role="alert">
                    Could not refresh kitchen status: {statusRefreshError}
                    <button
                      type="button"
                      className="staff-text-button"
                      onClick={() => void refreshSubmittedStatuses(order.id, order.diningTableId)}
                    >
                      Try again
                    </button>
                  </p>
                )}

                {orderIsActive && (
                  <div className="order-review-heading">
                    <h3>{order.status === "Sent" ? "Continue this order" : "Review order before sending"}</h3>
                    <p>{order.status === "Sent"
                      ? "This order is still active. Submitted dishes are locked; add a new line for extra portions or special requests."
                      : "Review each dish, quantity, and special request before sending the order."}</p>
                  </div>
                )}

                {order.status === "Closed" && (
                  <p className="staff-help">This order is closed and cannot be changed.</p>
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
                      const editable = orderIsActive && item.kitchenSubmissionId === null;
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
                              <span
                                className={`kitchen-preparation-status status-${item.preparationStatus.toLowerCase()}`}
                                aria-label={`Kitchen status: ${item.preparationStatus}`}
                              >
                                {item.preparationStatus === "New"
                                  ? "Submitted · New"
                                  : `Kitchen · ${item.preparationStatus}`}
                              </span>
                            )}
                          </div>
                          <label>
                            Quantity
                            <input
                              type="number"
                              inputMode="numeric"
                              step="any"
                              disabled={!editable || busyItemId === item.id || savingItem || sending}
                              value={draft.quantity}
                              aria-invalid={Boolean(itemValidationErrors[item.id])}
                              aria-describedby={itemValidationErrors[item.id]
                                ? `order-item-${item.id}-quantity-error`
                                : undefined}
                              onChange={(event) => {
                                const nextQuantity = event.target.value;
                                setItemDrafts((current) => ({
                                  ...current,
                                  [item.id]: { ...draft, quantity: nextQuantity },
                                }));
                                if (isPositiveWholeNumber(nextQuantity)) {
                                  setItemValidationErrors((current) => {
                                    const next = { ...current };
                                    delete next[item.id];
                                    return next;
                                  });
                                }
                              }}
                            />
                            {itemValidationErrors[item.id] && (
                              <span
                                className="order-field-error"
                                id={`order-item-${item.id}-quantity-error`}
                                role="alert"
                              >
                                {itemValidationErrors[item.id]}
                              </span>
                            )}
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
                              isPositiveWholeNumber(draft.quantity)
                                ? Number(draft.quantity)
                                : item.quantity
                            ))}
                          </span>
                          {orderIsActive && !editable && (
                            <div className="order-item-actions">
                              <button
                                type="button"
                                className="secondary-button"
                                disabled={savingItem || sending || busyItemId !== null ||
                                  !availableDishes.some((dish) => dish.id === item.dishId)}
                                onClick={() => focusAddDish(item.dishId)}
                              >
                                Add more
                              </button>
                            </div>
                          )}
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
                    <form ref={addDishFormRef} className="order-add-form" onSubmit={(event) => void addItem(event)}>
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
                  <p className="staff-help" role="status">
                    All current items have been sent. This order stays active until payment; use Add more dishes for new requests.
                  </p>
                )}
              </section>
            )}
          </>
        )}
      </main>
      {cancelDialogOpen && order && (
        <WaiterCancelDialog
          orderId={order.id}
          tableNumber={activeTable?.tableNumber}
          seatNumber={order.seatNumber}
          itemCount={order.items.length}
          reason={cancelReason}
          confirmed={cancelConfirmed}
          validationError={cancelValidationError}
          cancelling={cancelling}
          canCancel={canCancelOrder}
          restrictionMessage={hasSubmittedItems
            ? "This order already contains items sent to the kitchen. Kitchen-submitted work cannot be withdrawn from the waiter screen."
            : "Only an active order that has not been sent to the kitchen can be cancelled."}
          onReasonChange={(value: string) => {
            setCancelReason(value);
            setCancelValidationError(null);
          }}
          onConfirmedChange={(value: boolean) => {
            setCancelConfirmed(value);
            setCancelValidationError(null);
          }}
          onClose={closeCancelDialog}
          onSubmit={(event: FormEvent<HTMLFormElement>) => void submitCancellation(event)}
        />
      )}
    </div>
  );
}

export default WaiterOrderPage;
