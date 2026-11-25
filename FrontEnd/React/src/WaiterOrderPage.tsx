import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Link, useLocation } from "react-router-dom";
import {
  ApiError,
  addOrderItem,
  cancelWaiterOrder,
  createWaiterBill,
  getCurrentAccount,
  getMenuDishes,
  getOrderDetails,
  getWaiterOrderHistory,
  getWaiterOrderSubmissions,
  getWaiterBill,
  getWaiterNotifications,
  markWaiterItemServed,
  getWaiterTables,
  openOrCreateOrder,
  recordWaiterPayment,
  removeOrderItem,
  sendOrder,
  updateOrderItem,
} from "./services/api";
import type {
  BillDetailsResponse,
  BillPaymentMethod,
  KitchenSubmissionResponse,
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
import "./waiter-billing.css";
import "./waiter-order-history.css";

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

function formatBillAmount(amount: number, currencyCode: string): string {
  try {
    return new Intl.NumberFormat("ru-RU", {
      style: "currency",
      currency: currencyCode,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currencyCode}`;
  }
}

function formatTimelineDate(value: string | null): string {
  return value ? new Date(value).toLocaleString() : "Time not provided by the current API";
}

function createExternalPaymentReference(billId: number): string {
  const uniquePart = typeof crypto !== "undefined" && typeof crypto.randomUUID === "function"
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;
  return `waiter-${billId}-${uniquePart}`;
}

type PaymentReview = {
  amount: number;
  method: BillPaymentMethod;
  externalReference: string;
};

function WaiterOrderPage() {
  const location = useLocation();
  const waiterPage = location.hash === "#waiter-active-orders"
    ? "active"
    : location.hash === "#waiter-order-history"
      ? "history"
      : location.hash === "#waiter-billing"
        ? "payments"
        : "new";
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
  const [historySearch, setHistorySearch] = useState("");
  const [historyStatusFilter, setHistoryStatusFilter] = useState<"All" | "Closed" | "Legacy">("All");
  const [historyTableFilter, setHistoryTableFilter] = useState("all");
  const [historyDateFilter, setHistoryDateFilter] = useState("");
  const [orderSubmissions, setOrderSubmissions] = useState<KitchenSubmissionResponse[]>([]);
  const [timelineLoading, setTimelineLoading] = useState(false);
  const [timelineError, setTimelineError] = useState<string | null>(null);
  const timelineRequestVersion = useRef(0);
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
  const [bill, setBill] = useState<BillDetailsResponse | null>(null);
  const [billLoading, setBillLoading] = useState(false);
  const [billCreating, setBillCreating] = useState(false);
  const [billError, setBillError] = useState<string | null>(null);
  const [paymentAmount, setPaymentAmount] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<BillPaymentMethod>("Cash");
  const [paymentReview, setPaymentReview] = useState<PaymentReview | null>(null);
  const [paymentSubmitting, setPaymentSubmitting] = useState(false);
  const [paymentError, setPaymentError] = useState<string | null>(null);
  const [paymentNotice, setPaymentNotice] = useState<string | null>(null);
  const [paymentValidationError, setPaymentValidationError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [statusRefreshError, setStatusRefreshError] = useState<string | null>(null);
  const operationLock = useRef(false);
  const statusRefreshInFlight = useRef(false);
  const addDishFormRef = useRef<HTMLFormElement>(null);
  const orderIsActive = order !== null && (order.status === "Open" || order.status === "Sent");
  const servedItemCount = order?.items.filter((item) =>
    item.kitchenSubmissionId !== null &&
    item.preparationStatus === "Ready" &&
    item.serviceStatus === "Served" &&
    item.servedAtUtc !== null).length ?? 0;
  const allItemsServed = !!order && order.items.length > 0 &&
    servedItemCount === order.items.length;
  const pendingItems = order?.items.filter((item) => item.kitchenSubmissionId === null) ?? [];
  const hasSubmittedItems = order?.items.some((item) =>
    item.kitchenSubmissionId !== null || item.preparationStatus !== "New") ?? false;
  const canCancelOrder = orderIsActive && !hasSubmittedItems && !bill;
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
  const activeOrderEntries = useMemo(
    () => orderHistory.filter((entry) => entry.status === "Open" || entry.status === "Sent"),
    [orderHistory],
  );
  const historicalOrderEntries = useMemo(
    () => orderHistory.filter((entry) => entry.status === "Closed" || entry.status === "Legacy"),
    [orderHistory],
  );
  const filteredHistoricalOrders = useMemo(() => {
    const query = historySearch.trim().toLocaleLowerCase();
    return historicalOrderEntries.filter((entry) => {
      if (historyStatusFilter !== "All" && entry.status !== historyStatusFilter) return false;
      if (historyTableFilter !== "all" && entry.diningTableId !== Number(historyTableFilter)) return false;
      if (historyDateFilter) {
        const created = new Date(entry.createdAtUtc);
        const localDate = `${created.getFullYear()}-${String(created.getMonth() + 1).padStart(2, "0")}-${String(created.getDate()).padStart(2, "0")}`;
        if (localDate !== historyDateFilter) return false;
      }
      if (!query) return true;
      const table = tables.find((candidate) => candidate.id === entry.diningTableId);
      const searchable = [
        String(entry.id),
        `#${entry.id}`,
        entry.status,
        table ? `table ${table.tableNumber}` : "",
        table ? String(table.tableNumber) : "",
        entry.seatNumber != null ? `seat ${entry.seatNumber}` : "",
        entry.seatNumber != null ? String(entry.seatNumber) : "",
      ].join(" ").toLocaleLowerCase();
      return searchable.includes(query);
    });
  }, [historyDateFilter, historySearch, historyStatusFilter, historyTableFilter, historicalOrderEntries, tables]);
  const orderTimelineEvents = useMemo(() => {
    if (!order) return [];
    const events: Array<{
      id: string;
      title: string;
      description: string;
      timestamp: string | null;
      kind: "created" | "kitchen" | "bill" | "payment" | "closed";
    }> = [{
      id: `order-created-${order.id}`,
      title: "Order created",
      description: `Order #${order.id} was opened${order.seatNumber != null ? ` for seat ${order.seatNumber}` : ""}.`,
      timestamp: order.createdAtUtc,
      kind: "created",
    }];

    if (orderSubmissions.length > 0) {
      orderSubmissions
        .filter((submission) => submission.orderId === order.id && submission.diningTableId === order.diningTableId)
        .forEach((submission) => {
          const itemSummary = submission.items
            .map((item) => `${item.quantity} × ${item.dishName}`)
            .join(", ");
          events.push({
            id: `kitchen-submission-${submission.id}`,
            title: `Sent to kitchen · Batch #${submission.id}`,
            description: itemSummary || "Kitchen submission recorded.",
            timestamp: submission.sentAtUtc,
            kind: "kitchen",
          });
        });
    } else if (order.sentAtUtc) {
      events.push({
        id: `order-sent-${order.id}`,
        title: "Sent to kitchen",
        description: "The order was submitted to the kitchen.",
        timestamp: order.sentAtUtc,
        kind: "kitchen",
      });
    }

    if (bill && bill.orderId === order.id && bill.diningTableId === order.diningTableId) {
      events.push({
        id: `bill-created-${bill.id}`,
        title: `Bill prepared · #${bill.id}`,
        description: `Total ${formatBillAmount(bill.totalAmount, bill.currencyCode)}.`,
        timestamp: bill.createdAtUtc,
        kind: "bill",
      });
      bill.payments.forEach((payment) => {
        events.push({
          id: `payment-${payment.id}`,
          title: `Payment recorded · ${payment.method}`,
          description: `${formatBillAmount(payment.amount, bill.currencyCode)} · Reference ${payment.externalReference || "not supplied"}.`,
          timestamp: payment.recordedAtUtc,
          kind: "payment",
        });
      });
      if (bill.paymentStatus === "Paid" || order.paidAtUtc) {
        const lastPayment = bill.payments[bill.payments.length - 1];
        events.push({
          id: `order-paid-${order.id}`,
          title: "Payment completed · Order closed",
          description: "The full bill balance has been recorded.",
          timestamp: order.paidAtUtc ?? lastPayment?.recordedAtUtc ?? null,
          kind: "closed",
        });
      }
    } else if (order.paidAtUtc) {
      events.push({
        id: `order-paid-${order.id}`,
        title: "Payment completed · Order closed",
        description: "The order was marked as paid by the backend.",
        timestamp: order.paidAtUtc,
        kind: "closed",
      });
    } else if (order.status === "Closed") {
      events.push({
        id: `order-closed-${order.id}`,
        title: "Order closed",
        description: "The order is closed. The current API does not return a close timestamp or cancellation reason for this order.",
        timestamp: null,
        kind: "closed",
      });
    }

    return events.sort((first, second) => {
      if (!first.timestamp && !second.timestamp) return 0;
      if (!first.timestamp) return 1;
      if (!second.timestamp) return -1;
      return Date.parse(first.timestamp) - Date.parse(second.timestamp);
    });
  }, [bill, order, orderSubmissions]);

  useEffect(() => {
    const targetId = location.hash.replace(/^#/, "");
    if (!targetId || loading || authorized !== true) return;

    const frame = window.requestAnimationFrame(() => {
      document.getElementById(targetId)?.scrollIntoView({
        behavior: "smooth",
        block: "start",
      });
    });

    return () => window.cancelAnimationFrame(frame);
  }, [location.hash, loading, authorized, order?.id, bill?.id, refreshingTables]);

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

  const refreshOrderSubmissions = useCallback(async (orderId: number, expectedTableId: number) => {
    const requestVersion = ++timelineRequestVersion.current;
    setTimelineLoading(true);
    setTimelineError(null);
    try {
      const submissions = await getWaiterOrderSubmissions(orderId);
      if (requestVersion !== timelineRequestVersion.current) return;
      if (submissions.some((submission) =>
        submission.orderId !== orderId || submission.diningTableId !== expectedTableId)) {
        throw new Error("The timeline API returned a submission for a different order or table.");
      }
      setOrderSubmissions(submissions.sort(
        (first, second) => Date.parse(first.sentAtUtc) - Date.parse(second.sentAtUtc),
      ));
    } catch (caughtError) {
      if (requestVersion === timelineRequestVersion.current) {
        setOrderSubmissions([]);
        setTimelineError(getErrorMessage(caughtError, "Could not load this order's kitchen timeline."));
      }
    } finally {
      if (requestVersion === timelineRequestVersion.current) setTimelineLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!authorized || !order) {
      timelineRequestVersion.current += 1;
      setOrderSubmissions([]);
      setTimelineError(null);
      setTimelineLoading(false);
      return;
    }
    void refreshOrderSubmissions(order.id, order.diningTableId);
  }, [authorized, order?.id, order?.diningTableId, refreshOrderSubmissions]);

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

  const currentOrderId = order?.id ?? null;
  const currentOrderTableId = order?.diningTableId ?? null;

  // Recheck actual served states on entry to billing, even when the waiter
  // has been on a different tab while kitchen/service events were processed.
  useEffect(() => {
    if (!authorized || waiterPage !== "payments" ||
        currentOrderId === null || currentOrderTableId === null) return;
    void refreshOrder(currentOrderId, currentOrderTableId)
      .catch((caughtError: unknown) => setBillError(
        getErrorMessage(caughtError, "Could not verify which dishes were served.")));
  }, [authorized, waiterPage, currentOrderId, currentOrderTableId, refreshOrder]);

  useEffect(() => {
    let active = true;
    if (!authorized || currentOrderId === null) {
      setBill(null);
      setBillError(null);
      setBillLoading(false);
      setPaymentAmount("");
      setPaymentReview(null);
      setPaymentError(null);
      setPaymentNotice(null);
      setPaymentValidationError(null);
      return () => { active = false; };
    }

    setBill(null);
    setBillError(null);
    setBillLoading(true);
    setPaymentAmount("");
    setPaymentReview(null);
    setPaymentError(null);
    setPaymentNotice(null);
    setPaymentValidationError(null);

    void getWaiterBill(currentOrderId)
      .then((loadedBill) => {
        if (!active) return;
        if (loadedBill.orderId !== currentOrderId ||
            loadedBill.diningTableId !== currentOrderTableId) {
          throw new Error("The bill returned by the API does not match the selected order and table.");
        }
        setBill(loadedBill);
        setPaymentAmount(loadedBill.balanceDue > 0
          ? loadedBill.balanceDue.toFixed(2)
          : "0.00");
      })
      .catch((caughtError: unknown) => {
        if (!active) return;
        if (caughtError instanceof ApiError && caughtError.status === 404) {
          // A 404 means this order does not have a bill yet.
          setBill(null);
          setBillError(null);
        } else {
          setBillError(getErrorMessage(caughtError, "Could not load this order's bill."));
        }
      })
      .finally(() => {
        if (active) setBillLoading(false);
      });

    return () => { active = false; };
  }, [authorized, currentOrderId, currentOrderTableId]);

  const refreshBill = async (orderId: number, expectedTableId: number) => {
    setBillLoading(true);
    setBillError(null);
    try {
      const loadedBill = await getWaiterBill(orderId);
      if (loadedBill.orderId !== orderId || loadedBill.diningTableId !== expectedTableId) {
        throw new Error("The bill returned by the API does not match the selected order and table.");
      }
      setBill(loadedBill);
      setPaymentAmount(loadedBill.balanceDue > 0 ? loadedBill.balanceDue.toFixed(2) : "0.00");
      setPaymentReview(null);
      setPaymentError(null);
      setPaymentValidationError(null);
      return loadedBill;
    } catch (caughtError) {
      setBillError(getErrorMessage(caughtError, "Could not refresh this bill."));
      throw caughtError;
    } finally {
      setBillLoading(false);
    }
  };

  const prepareBill = async () => {
    if (!order || !orderIsActive || !allItemsServed || operationLock.current || billCreating) return;
    operationLock.current = true;
    setBillCreating(true);
    setBillLoading(true);
    setBillError(null);
    setPaymentError(null);
    setPaymentNotice(null);
    try {
      // Do not rely on a possibly stale React snapshot when creating a bill.
      const latest = await getOrderDetails(order.id);
      if (latest.id !== order.id || latest.diningTableId !== order.diningTableId) {
        throw new Error("The selected order changed. Select it again before billing.");
      }
      setOrder(latest);
      if (!(["Open", "Sent"].includes(latest.status) &&
          latest.items.length > 0 && latest.items.every((item) =>
            item.kitchenSubmissionId !== null &&
            item.preparationStatus === "Ready" &&
            item.serviceStatus === "Served" &&
            item.servedAtUtc !== null))) {
        throw new Error("All dishes must be marked Served before a bill can be created.");
      }
      const createdBill = await createWaiterBill(order.id, "RUB");
      if (createdBill.orderId !== order.id || createdBill.diningTableId !== order.diningTableId) {
        throw new Error("The billing API returned a bill for a different order or table.");
      }
      setBill(createdBill);
      setPaymentAmount(createdBill.balanceDue > 0
        ? createdBill.balanceDue.toFixed(2)
        : "0.00");
      setNotice(`Bill #${createdBill.id} is ready for order #${order.id}.`);
      // Re-read the server snapshot so the preview always reflects persisted values.
      await refreshBill(order.id, order.diningTableId);
    } catch (caughtError) {
      setBillError(getErrorMessage(caughtError, "Could not prepare this bill."));
    } finally {
      operationLock.current = false;
      setBillCreating(false);
      setBillLoading(false);
    }
  };

  const reviewPaymentEntry = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!bill || bill.balanceDue <= 0 || paymentSubmitting) return;
    if (!allItemsServed) {
      setPaymentValidationError("All dishes must be served before payment can be recorded.");
      return;
    }
    const normalizedAmount = paymentAmount.trim();
    if (!/^(?:\d+)(?:\.\d{1,2})?$/.test(normalizedAmount)) {
      setPaymentValidationError("Enter an amount with no more than two decimal places.");
      return;
    }
    const amount = Number(normalizedAmount);
    if (!Number.isFinite(amount) || amount <= 0) {
      setPaymentValidationError("Payment amount must be greater than zero.");
      return;
    }
    if (amount > bill.balanceDue + 0.000001) {
      setPaymentValidationError(`Payment cannot exceed the remaining balance of ${formatBillAmount(bill.balanceDue, bill.currencyCode)}.`);
      return;
    }
    setPaymentValidationError(null);
    setPaymentError(null);
    setPaymentNotice(null);
    setPaymentReview({
      amount: Math.round(amount * 100) / 100,
      method: paymentMethod,
      externalReference: createExternalPaymentReference(bill.id),
    });
  };

  const confirmPayment = async () => {
    if (!bill || !order || !paymentReview || paymentSubmitting || operationLock.current) return;
    if (!allItemsServed) {
      setPaymentError("All dishes must be served before a payment is recorded.");
      return;
    }
    operationLock.current = true;
    setPaymentSubmitting(true);
    setPaymentError(null);
    setPaymentNotice(null);
    try {
      const result = await recordWaiterPayment(
        bill.id,
        paymentReview.amount,
        paymentReview.method,
        paymentReview.externalReference,
      );
      if (!result.payment || !result.bill ||
          result.bill.id !== bill.id ||
          result.bill.orderId !== order.id ||
          result.bill.diningTableId !== order.diningTableId ||
          result.payment.amount !== paymentReview.amount ||
          result.payment.method !== paymentReview.method ||
          result.payment.externalReference !== paymentReview.externalReference) {
        throw new Error("The API response did not confirm the payment details for this bill.");
      }

      setBill(result.bill);
      setPaymentReview(null);
      setPaymentAmount(result.bill.balanceDue > 0
        ? result.bill.balanceDue.toFixed(2)
        : "0.00");
      setPaymentValidationError(null);
      setPaymentNotice(result.alreadyRecorded
        ? `Payment of ${formatBillAmount(result.payment.amount, result.bill.currencyCode)} was already recorded. The bill has been refreshed.`
        : `Payment of ${formatBillAmount(result.payment.amount, result.bill.currencyCode)} was recorded successfully.`);
      if (result.bill.paymentStatus === "Paid") {
        setOrder((current) => current?.id === order.id
          ? { ...current, status: "Closed" }
          : current);
        setOrderHistory((current) => current.map((entry) => entry.id === order.id
          ? { ...entry, status: "Closed" }
          : entry));
        const availableSeatLabel = order.seatNumber != null ? `Seat ${order.seatNumber}` : "The seat";
        setNotice(`Order #${order.id} is fully paid and closed. It has moved to Order history, and ${availableSeatLabel} is available for a new order.`);
        void refreshOrderHistory(false);
      }
    } catch (caughtError) {
      setPaymentError(`${getErrorMessage(caughtError, "Could not record this payment.")} You can retry safely; the same payment reference will be reused.`);
    } finally {
      operationLock.current = false;
      setPaymentSubmitting(false);
    }
  };

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
              serviceStatus: latestItem.serviceStatus,
              servedAtUtc: latestItem.servedAtUtc,
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
      if (order?.id === notification.orderId) {
        // Serving is already committed; a refresh failure must not report
        // a false failure of the action itself.
        void refreshOrder(order.id, order.diningTableId).catch(
          (caughtError: unknown) => setStatusRefreshError(
            getErrorMessage(caughtError, "Dish was served, but order refresh failed.")));
      }
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

  const openTable = async (table: WaiterTableResponse, seatNumber: number) => {
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
      if (opened.reused) {
        void refreshOrderHistory(false);
        throw new Error(`Seat ${seatNumber} already has active order #${opened.orderId}. Open it from the Active orders list; New order only creates an order on an empty seat.`);
      }
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
    if (!order || !orderIsActive || bill || billLoading || operationLock.current) return;
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
    if (!orderIsActive || bill || billLoading || item.kitchenSubmissionId !== null) {
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
    if (!orderIsActive || bill || billLoading || !item || item.kitchenSubmissionId !== null) {
      setError(bill
        ? "This order already has a bill. Order changes are locked to keep the bill totals consistent."
        : "Submitted dishes cannot be removed from a kitchen ticket.");
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
    if (!order || operationLock.current) return;
    setCancelReason("");
    setCancelConfirmed(false);
    setCancelValidationError(null);
    setCancelDialogOpen(true);
  };

  const closeCancelDialog = useCallback(() => {
    if (cancelling) return;
    setCancelDialogOpen(false);
    setCancelValidationError(null);
  }, [cancelling]);

  const submitCancellation = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order || !canCancelOrder || operationLock.current) {
      setCancelValidationError(
        bill
          ? "A bill has already been prepared for this order. Cancellation is disabled to protect the bill and payment record."
          : hasSubmittedItems
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
    if (!order || !orderIsActive || bill || billLoading || operationLock.current || pendingItems.length === 0) return;
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
        <header className="admin-header waiter-page-header">
          <div>
            <p className="eyebrow">FLOOR / WAITER</p>
            <h1>{waiterPage === "new" ? "New order" : waiterPage === "active" ? "Active orders" : waiterPage === "history" ? "Order history" : "Bills & payments"}</h1>
            <p className="subtitle">{waiterPage === "new" ? "Select an assigned table and an empty seat to start a new order." : waiterPage === "active" ? "Review and manage orders currently in service." : waiterPage === "history" ? "Search completed orders and review their activity." : "Prepare bills, record payments and confirm balances."}</p>
          </div>
          <WorkspaceSessionActions />
        </header>

        {error && <div className="admin-error" role="alert">{error}</div>}
        {notice && <div className="admin-success" role="status">{notice}</div>}
        {authorized && waiterPage === "active" && (
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
            {waiterPage === "new" && (
            <section id="waiter-new-order" className="staff-card waiter-sidebar-target waiter-navigation-page">
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
                      onClick={() => selectTable(table)}
                    >
                      <strong>Table {table.tableNumber}</strong>
                      <span>{table.seatCount} seats</span>
                    </button>
                  ))}
                </div>
              )}
              {selectedTableId !== null && activeTable && (
                <div className="order-seat-selector">
                  <h3>Seats at Table {activeTable.tableNumber}</h3>
                  <p className="staff-help">Choose New order only for an empty seat. Existing active orders are opened separately below.</p>
                  {historyError && <p className="staff-notice error" role="alert">Seat availability cannot be verified until order history loads successfully. Refresh history before creating an order.</p>}
                  <div className="order-seat-list">
                    {Array.from({ length: activeTable.seatCount }, (_, index) => index + 1).map((seat) => {
                      const existing = activeOrderEntries.find((entry) =>
                        entry.diningTableId === activeTable.id && entry.seatNumber === seat);
                      return existing ? (
                        <div
                          key={seat}
                          className="order-seat-button occupied"
                          aria-label={`Seat ${seat} is occupied by active order ${existing.id}`}
                        >
                          <strong>Seat {seat}</strong>
                          <span>Occupied · Order #{existing.id} · {existing.status}</span>
                        </div>
                      ) : (
                        <button
                          type="button"
                          key={seat}
                          className="order-seat-button new-order"
                          aria-label={`Create a new order for table ${activeTable.tableNumber}, seat ${seat}`}
                          disabled={opening || refreshingTables || savingItem || sending || busyItemId !== null || Boolean(historyError) || (historyLoading && orderHistory.length === 0)}
                          onClick={() => void openTable(activeTable, seat)}
                        >
                          <strong>Seat {seat}</strong>
                          <span>＋ New order</span>
                        </button>
                      );
                    })}
                  </div>
                </div>
              )}
            </section>
            )}

            {waiterPage === "active" && (
            <section id="waiter-active-orders" className="staff-card waiter-sidebar-target" aria-labelledby="waiter-active-orders-title">
              <div className="staff-card-heading">
                <div>
                  <p className="eyebrow">CURRENT WORK</p>
                  <h2 id="waiter-active-orders-title">Active orders</h2>
                  <p className="staff-help">Open an existing order here. The New order buttons above never reopen an active order.</p>
                </div>
                <span className="waiter-history-count">{activeOrderEntries.length} active</span>
              </div>
              {historyLoading && orderHistory.length === 0 ? (
                <p className="staff-empty" role="status" aria-busy="true">Loading active orders...</p>
              ) : historyError && orderHistory.length === 0 ? (
                <p className="staff-empty" role="status">Active orders cannot be verified until order history loads successfully.</p>
              ) : activeOrderEntries.length === 0 ? (
                <p className="staff-empty" role="status">No active orders. Empty seats are available for new orders.</p>
              ) : (
                <div className="waiter-order-history">
                  {activeOrderEntries.map((entry) => {
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
                        <span>{table ? `Table ${table.tableNumber}` : "Assigned table"}{entry.seatNumber != null ? ` · Seat ${entry.seatNumber}` : ""}</span>
                        <time dateTime={entry.createdAtUtc}>{new Date(entry.createdAtUtc).toLocaleString()}</time>
                        <span className="waiter-order-history-action">{entry.status} · {order?.id === entry.id ? "Viewing" : "Open order"}</span>
                      </button>
                    );
                  })}
                </div>
              )}
            </section>
            )}

            {waiterPage === "history" && (
            <section id="waiter-order-history" className="staff-card waiter-history-card waiter-sidebar-target" aria-labelledby="waiter-order-history-title">
              <div className="staff-card-heading">
                <div>
                  <p className="eyebrow">ORDER HISTORY</p>
                  <h2 id="waiter-order-history-title">Closed orders</h2>
                  <p className="staff-help">Orders that are closed by completed payment or cancellation move here automatically.</p>
                </div>
                <div className="waiter-history-heading-actions">
                  <span className="waiter-history-count">{historicalOrderEntries.length} records</span>
                  <button
                    type="button"
                    className="secondary-button"
                    disabled={historyLoading || opening}
                    onClick={() => void refreshOrderHistory()}
                  >
                    {historyLoading ? "Refreshing..." : "Refresh history"}
                  </button>
                </div>
              </div>
              <div className="waiter-history-filters">
                <label className="waiter-history-search">
                  Search orders
                  <input
                    type="search"
                    value={historySearch}
                    onChange={(event) => setHistorySearch(event.target.value)}
                    placeholder="Order ID, table number or seat"
                  />
                </label>
                <label>
                  Status
                  <select value={historyStatusFilter} onChange={(event) => setHistoryStatusFilter(event.target.value as "All" | "Closed" | "Legacy")}>
                    <option value="All">All history</option>
                    <option value="Closed">Closed</option>
                    <option value="Legacy">Legacy</option>
                  </select>
                </label>
                <label>
                  Table
                  <select value={historyTableFilter} onChange={(event) => setHistoryTableFilter(event.target.value)}>
                    <option value="all">All assigned tables</option>
                    {tables.map((table) => <option key={table.id} value={table.id}>Table {table.tableNumber}</option>)}
                  </select>
                </label>
                <label>
                  Created on
                  <input type="date" value={historyDateFilter} onChange={(event) => setHistoryDateFilter(event.target.value)} />
                </label>
                <button
                  type="button"
                  className="staff-text-button waiter-history-clear"
                  onClick={() => { setHistorySearch(""); setHistoryStatusFilter("All"); setHistoryTableFilter("all"); setHistoryDateFilter(""); }}
                  disabled={!historySearch && historyStatusFilter === "All" && historyTableFilter === "all" && !historyDateFilter}
                >
                  Clear filters
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
              {historyLoading && orderHistory.length === 0 ? (
                <div className="waiter-history-loading" role="status" aria-busy="true"><span className="admin-spinner" /> Loading historical orders...</div>
              ) : historyError && orderHistory.length === 0 ? (
                <div className="waiter-history-empty" role="status">
                  <strong>History could not be loaded</strong>
                  <span>Retry the order history request before treating this list as empty.</span>
                </div>
              ) : historicalOrderEntries.length === 0 ? (
                <div className="waiter-history-empty" role="status">
                  <strong>No closed orders yet</strong>
                  <span>When an order is closed by completed payment or cancellation, it will appear here automatically.</span>
                </div>
              ) : filteredHistoricalOrders.length === 0 ? (
                <div className="waiter-history-empty" role="status">
                  <strong>No matching orders</strong>
                  <span>Try changing the search text or filters.</span>
                </div>
              ) : (
                <div className="waiter-order-history">
                  {filteredHistoricalOrders.map((entry) => {
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
                        <span>{table ? `Table ${table.tableNumber}` : "Assigned table"}{entry.seatNumber != null ? ` · Seat ${entry.seatNumber}` : " · No seat assigned"}</span>
                        <time dateTime={entry.createdAtUtc}>{new Date(entry.createdAtUtc).toLocaleString()}</time>
                        <span className="waiter-order-history-action">{entry.status} · {order?.id === entry.id ? "Viewing" : "View details"}</span>
                      </button>
                    );
                  })}
                </div>
              )}
            </section>
            )}

            {((waiterPage === "new" && order) || (waiterPage === "active" && orderIsActive) || (waiterPage === "history" && order && !orderIsActive)) && order && activeTable && !refreshingTables && (
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
                    {orderIsActive && !bill && (
                      <button
                        type="button"
                        className="secondary-button"
                        disabled={billLoading || savingItem || sending || busyItemId !== null || availableDishes.length === 0}
                        onClick={() => focusAddDish()}
                      >
                        Add more dishes
                      </button>
                    )}
                    {orderIsActive && (
                      <button
                        type="button"
                        className="order-cancel-button"
                        disabled={opening || billLoading || Boolean(bill) || savingItem || sending || cancelling || busyItemId !== null}
                        aria-describedby={bill
                          ? "waiter-bill-locked"
                          : hasSubmittedItems ? "waiter-cancel-unavailable" : undefined}
                        title={bill
                          ? "A bill has been prepared; cancellation is disabled"
                          : hasSubmittedItems
                            ? "View why this order cannot be cancelled"
                            : "Cancel this unpaid order"}
                        onClick={openCancelDialog}
                      >
                        Cancel order
                      </button>
                    )}
                  </div>
                </div>

                <section className="waiter-order-timeline" aria-labelledby="waiter-order-timeline-title">
                  <div className="waiter-order-timeline-heading">
                    <div>
                      <p className="eyebrow">ORDER ACTIVITY</p>
                      <h3 id="waiter-order-timeline-title">Order change timeline</h3>
                    </div>
                    <button
                      type="button"
                      className="secondary-button"
                      disabled={timelineLoading}
                      onClick={() => void refreshOrderSubmissions(order.id, order.diningTableId)}
                    >
                      {timelineLoading ? "Refreshing..." : "Refresh timeline"}
                    </button>
                  </div>
                  {timelineLoading && <p className="staff-help" role="status" aria-busy="true">Loading kitchen activity...</p>}
                  {timelineError && <p className="staff-notice error" role="alert">{timelineError}</p>}
                  <ol className="waiter-order-timeline-list">
                    {orderTimelineEvents.map((event) => (
                      <li key={event.id} className={`waiter-order-timeline-event ${event.kind}`}>
                        <span className="waiter-order-timeline-marker" aria-hidden="true" />
                        <div className="waiter-order-timeline-content">
                          <div className="waiter-order-timeline-event-heading">
                            <strong>{event.title}</strong>
                            <time>{formatTimelineDate(event.timestamp)}</time>
                          </div>
                          <p>{event.description}</p>
                        </div>
                      </li>
                    ))}
                  </ol>
                  <p className="waiter-order-timeline-note">
                    This timeline combines order creation, kitchen submissions, bill creation and payment events returned by the current API. Item additions, edits, removals and cancellation reasons are stored by the backend, but the current waiter API does not expose those audit records for reading.
                  </p>
                </section>

                {orderIsActive && hasSubmittedItems && (
                  <p className="staff-notice warning" id="waiter-cancel-unavailable" role="status">
                    This order contains items already sent to the kitchen. The current waiter cancellation endpoint
                    cannot withdraw submitted kitchen items, so cancellation is unavailable.
                  </p>
                )}
                {orderIsActive && billLoading && (
                  <p className="staff-notice warning" role="status">
                    Checking whether this order already has a bill. Order changes and cancellation are temporarily locked.
                  </p>
                )}
                {orderIsActive && bill && (
                  <p className="staff-notice warning" id="waiter-bill-locked" role="status">
                    A bill has been prepared for this order. Item changes, additional dishes and cancellation are locked so the bill totals remain consistent.
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
                      const editable = orderIsActive && !bill && !billLoading && item.kitchenSubmissionId === null;
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
                          {orderIsActive && !editable && !bill && !billLoading && (
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

                {orderIsActive && !bill && !billLoading && (
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
                    {bill
                      ? "All current items were sent and a bill has been prepared. Complete payment in the bill panel; new dishes are locked for this bill."
                      : "All current items have been sent. This order stays active until payment; use Add more dishes for new requests."}
                  </p>
                )}
              </section>
            )}
            {waiterPage === "payments" && (
            <div id="waiter-billing" className="waiter-billing-navigation-target waiter-sidebar-target waiter-navigation-page">
              {order && activeTable && !refreshingTables ? (
                <section className="staff-card waiter-billing-card" aria-labelledby="waiter-billing-heading">
                <div className="staff-card-heading waiter-billing-heading">
                  <div>
                    <p className="eyebrow">CHECKOUT / PAYMENT</p>
                    <h2 id="waiter-billing-heading">Bill preview & payment</h2>
                    <p className="staff-help">Order #{order.id} · Table {activeTable.tableNumber}{order.seatNumber ? ` · Seat ${order.seatNumber}` : ""}</p>
                  </div>
                  {bill && (
                    <button
                      type="button"
                      className="secondary-button"
                      disabled={billLoading || paymentSubmitting || billCreating}
                      onClick={() => void refreshBill(order.id, order.diningTableId).catch(() => undefined)}
                    >
                      {billLoading ? "Refreshing..." : "Refresh bill"}
                    </button>
                  )}
                </div>

                {billLoading && !bill ? (
                  <div className="waiter-billing-state" role="status" aria-busy="true">
                    <span className="admin-spinner" /> Checking bill details...
                  </div>
                ) : billError && !bill ? (
                  <div className="waiter-billing-state error" role="alert">
                    <p>{billError}</p>
                    <button type="button" className="secondary-button" onClick={() => void refreshBill(order.id, order.diningTableId).catch(() => undefined)}>
                      Try again
                    </button>
                  </div>
                ) : !bill ? (
                  <div className="waiter-billing-empty" role="status">
                    <div>
                      <strong>{order.items.length === 0
                        ? "This order has no items yet"
                        : allItemsServed ? "All dishes served — ready to bill"
                        : "Complete table service before billing"}</strong>
                      <p>{order.items.length === 0
                        ? "Add and serve at least one dish before preparing the bill."
                        : !allItemsServed
                          ? `${servedItemCount} of ${order.items.length} dish lines served. The bill becomes available once every dish has been marked Served.`
                          : "All dishes are served. Prepare the bill to review the total and collect payment."}</p>
                      {!allItemsServed && order.items.length > 0 && (
                        <ul className="waiter-unserved-dishes">
                          {order.items.filter((item) =>
                            item.serviceStatus !== "Served" || item.servedAtUtc === null ||
                            item.kitchenSubmissionId === null).map((item) => (
                              <li key={item.id}>{item.dishName} · Qty {item.quantity} — {item.kitchenSubmissionId === null
                                ? "Not sent to kitchen"
                                : item.preparationStatus !== "Ready" ? "Kitchen preparation pending" : "Not served"}</li>
                            ))}
                        </ul>
                      )}
                    </div>
                    <div className="waiter-billing-gate-actions">
                      {!allItemsServed && (
                        <button type="button" className="secondary-button" disabled={billCreating}
                          onClick={() => void refreshOrder(order.id, order.diningTableId).catch((caughtError: unknown) =>
                            setBillError(getErrorMessage(caughtError, "Could not refresh served statuses.")))}>
                          Refresh served status
                        </button>
                      )}
                      <button
                      type="button"
                      className="primary-button"
                      disabled={!orderIsActive || !allItemsServed || billCreating || billLoading}
                      onClick={() => void prepareBill()}
                    >
                      {billCreating ? "Preparing bill..." : "Prepare bill"}
                      </button>
                    </div>
                  </div>
                ) : (
                  <>
                    {billError && (
                      <p className="staff-notice error" role="alert">
                        {billError}
                        <button type="button" className="staff-text-button" onClick={() => void refreshBill(order.id, order.diningTableId).catch(() => undefined)}>
                          Try again
                        </button>
                      </p>
                    )}
                    <div className="waiter-bill-meta">
                      <span>Bill <strong>#{bill.id}</strong></span>
                      <span>Order <strong>#{bill.orderId}</strong></span>
                      <span>Currency <strong>{bill.currencyCode}</strong></span>
                      <span>Created <strong>{new Date(bill.createdAtUtc).toLocaleString()}</strong></span>
                      <span className={`waiter-bill-status status-${bill.paymentStatus.toLowerCase()}`}>
                        {bill.paymentStatus === "PartiallyPaid" ? "Partially paid" : bill.paymentStatus}
                      </span>
                    </div>

                    <div className="waiter-bill-details">
                      <div className="waiter-bill-table-wrap">
                        <table className="waiter-bill-table">
                          <thead>
                            <tr>
                              <th scope="col">Item</th>
                              <th scope="col">Qty</th>
                              <th scope="col">Unit price</th>
                              <th scope="col">Line total</th>
                            </tr>
                          </thead>
                          <tbody>
                            {bill.items.length === 0 ? (
                              <tr><td colSpan={4} className="waiter-bill-no-items">The API returned an empty bill.</td></tr>
                            ) : bill.items.map((line) => (
                              <tr key={line.orderItemId}>
                                <td>
                                  <strong>{line.dishName}</strong>
                                  {line.specialRequest && <small>{line.specialRequest}</small>}
                                </td>
                                <td>{line.quantity}</td>
                                <td>{formatBillAmount(line.unitPrice, bill.currencyCode)}</td>
                                <td>{formatBillAmount(line.lineTotal, bill.currencyCode)}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>

                      <aside className="waiter-bill-summary-panel" aria-label="Bill totals">
                        <p className="eyebrow">BILL SUMMARY</p>
                        <div className="waiter-bill-summary">
                          <dl>
                            <div><dt>Subtotal</dt><dd>{formatBillAmount(bill.subtotalAmount, bill.currencyCode)}</dd></div>
                            <div><dt>Tax</dt><dd>{formatBillAmount(bill.taxAmount, bill.currencyCode)}</dd></div>
                            <div><dt>Discount</dt><dd>{bill.discountAmount > 0 ? `−${formatBillAmount(bill.discountAmount, bill.currencyCode)}` : formatBillAmount(0, bill.currencyCode)}</dd></div>
                            <div className="grand-total"><dt>Total</dt><dd>{formatBillAmount(bill.totalAmount, bill.currencyCode)}</dd></div>
                            <div><dt>Amount paid</dt><dd>{formatBillAmount(bill.amountPaid, bill.currencyCode)}</dd></div>
                            <div className="balance-due"><dt>Balance due</dt><dd>{formatBillAmount(bill.balanceDue, bill.currencyCode)}</dd></div>
                          </dl>
                        </div>
                      </aside>
                    </div>

                    {bill.payments.length > 0 && (
                      <div className="waiter-payment-history">
                        <h3>Payment history</h3>
                        <div className="waiter-bill-table-wrap">
                          <table className="waiter-bill-table payment-history-table">
                            <thead><tr><th scope="col">Recorded</th><th scope="col">Method</th><th scope="col">Reference</th><th scope="col">Amount</th></tr></thead>
                            <tbody>
                              {bill.payments.map((payment) => (
                                <tr key={payment.id}>
                                  <td>{new Date(payment.recordedAtUtc).toLocaleString()}</td>
                                  <td>{payment.method}</td>
                                  <td>{payment.externalReference ?? "—"}</td>
                                  <td>{formatBillAmount(payment.amount, bill.currencyCode)}</td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </div>
                      </div>
                    )}

                    {paymentNotice && <p className="staff-notice success" role="status">{paymentNotice}</p>}
                    {paymentError && <p className="staff-notice error" role="alert">{paymentError}</p>}

                    {bill.paymentStatus === "Paid" || bill.balanceDue <= 0 ? (
                      <div className="waiter-payment-complete" role="status">
                        <strong>Payment complete</strong>
                        <span>This bill is fully paid. Order #{bill.orderId} is closed.</span>
                      </div>
                    ) : order.status === "Closed" ? (
                      <div className="waiter-billing-state error" role="status">
                        <strong>Payment cannot be recorded for this closed order.</strong>
                        <span>The backend only accepts payments while the order remains active. Contact a manager to resolve the outstanding balance.</span>
                      </div>
                    ) : !allItemsServed ? (
                      <div className="waiter-billing-state error" role="status">
                        <strong>Payment blocked until every dish is served.</strong>
                        <span>{servedItemCount} of {order.items.length} dish lines served. Refresh the order after serving the remaining dishes.</span>
                        <button type="button" className="secondary-button"
                          onClick={() => void refreshOrder(order.id, order.diningTableId).catch((caughtError: unknown) =>
                            setPaymentError(getErrorMessage(caughtError, "Could not refresh served statuses.")))}>
                          Refresh status
                        </button>
                      </div>
                    ) : paymentReview ? (
                      <div className="waiter-payment-review" aria-labelledby="waiter-payment-review-heading">
                        <div>
                          <p className="eyebrow">CONFIRM PAYMENT</p>
                          <h3 id="waiter-payment-review-heading">Review before recording</h3>
                        </div>
                        <dl>
                          <div><dt>Payment amount</dt><dd>{formatBillAmount(paymentReview.amount, bill.currencyCode)}</dd></div>
                          <div><dt>Method</dt><dd>{paymentReview.method}</dd></div>
                          <div><dt>Remaining after payment</dt><dd>{formatBillAmount(Math.max(0, bill.balanceDue - paymentReview.amount), bill.currencyCode)}</dd></div>
                        </dl>
                        <p className="waiter-payment-reference">Reference: {paymentReview.externalReference}</p>
                        <p className="waiter-payment-disclaimer">This records the payment in the restaurant system. It does not initiate a card transaction through a payment gateway.</p>
                        <div className="waiter-payment-actions">
                          <button
                            type="button"
                            className="secondary-button"
                            disabled={paymentSubmitting}
                            onClick={() => { setPaymentReview(null); setPaymentError(null); }}
                          >
                            Back to edit
                          </button>
                          <button type="button" className="primary-button" disabled={paymentSubmitting} onClick={() => void confirmPayment()}>
                            {paymentSubmitting ? "Recording payment..." : "Confirm payment"}
                          </button>
                        </div>
                      </div>
                    ) : (
                      <form className="waiter-payment-entry" onSubmit={reviewPaymentEntry}>
                        <div className="waiter-payment-entry-heading">
                          <div><p className="eyebrow">PAYMENT ENTRY</p><h3>Record a payment</h3></div>
                          <span>{bill.paymentStatus === "PartiallyPaid" ? "Outstanding balance" : "Awaiting payment"}</span>
                        </div>
                        <div className="waiter-payment-fields">
                          <label>
                            Amount ({bill.currencyCode})
                            <input
                              type="number"
                              min="0.01"
                              max={bill.balanceDue}
                              step="0.01"
                              inputMode="decimal"
                              required
                              value={paymentAmount}
                              onChange={(event) => { setPaymentAmount(event.target.value); setPaymentValidationError(null); }}
                            />
                          </label>
                          <label>
                            Payment method
                            <select value={paymentMethod} onChange={(event) => setPaymentMethod(event.target.value as BillPaymentMethod)}>
                              <option value="Cash">Cash</option>
                              <option value="Card">Card</option>
                              <option value="Other">Other</option>
                            </select>
                          </label>
                        </div>
                        {paymentValidationError && <p className="staff-notice error" role="alert">{paymentValidationError}</p>}
                        <p className="waiter-payment-disclaimer">Partial payments are supported. The order closes when the full balance is recorded. Card selection records the method only; it does not process a card transaction.</p>
                        <button type="submit" className="primary-button" disabled={paymentSubmitting || bill.balanceDue <= 0}>
                          Review payment
                        </button>
                      </form>
                    )}
                  </>
                )}
                </section>
              ) : (
                <section className="staff-card waiter-billing-empty" aria-labelledby="waiter-billing-empty-heading">
                  <div>
                    <p className="eyebrow">CHECKOUT / PAYMENT</p>
                    <h2 id="waiter-billing-empty-heading">Bills &amp; payments</h2>
                    <p className="staff-help">
                      Select an active order from the Active orders section to prepare its itemised bill,
                      review totals and record a payment.
                    </p>
                  </div>
                </section>
              )}
            </div>
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
          restrictionMessage={bill
            ? "A bill has already been prepared for this order. Cancellation is disabled to protect the bill and payment record."
            : hasSubmittedItems
              ? "This order contains items already submitted to the kitchen, which the cancellation action cannot withdraw."
              : "This order is no longer active and cannot be cancelled."}
          onReasonChange={(value) => {
            setCancelReason(value);
            setCancelValidationError(null);
          }}
          onConfirmedChange={(value) => {
            setCancelConfirmed(value);
            setCancelValidationError(null);
          }}
          onClose={closeCancelDialog}
          onSubmit={(event) => { void submitCancellation(event); }}
        />
      )}
    </div>
  );
}

export default WaiterOrderPage;
