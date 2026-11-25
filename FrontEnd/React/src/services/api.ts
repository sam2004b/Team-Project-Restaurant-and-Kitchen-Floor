export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");
const accessTokenStorageKey = "restaurant.accessToken";
const selectedRestaurantStorageKey = "restaurant.selectedRestaurantId";
export const sessionExpiredEvent = "restaurant:session-expired";
export const authorizationDeniedEvent = "restaurant:authorization-denied";

export interface LoginRestaurantOption {
    restaurantId: number;
    role: string;
}

export class ApiError extends Error {
    constructor(
        message: string,
        readonly status: number,
        readonly fieldErrors: Record<string, string[]> = {},
        readonly restaurants: LoginRestaurantOption[] = [],
    ) {
        super(message);
        this.name = "ApiError";
    }
}

async function throwApiError(response: Response): Promise<never> {
    const responseBody = await response.text();
    let message = `API request failed with status ${response.status}.`;
    let fieldErrors: Record<string, string[]> = {};
    let restaurants: LoginRestaurantOption[] = [];

    if (responseBody) {
        try {
            const payload: unknown = JSON.parse(responseBody);

            if (payload && typeof payload === "object") {
                const details = payload as {
                    message?: unknown;
                    error?: unknown;
                    detail?: unknown;
                    title?: unknown;
                    errors?: Record<string, unknown>;
                    restaurants?: unknown;
                };

                restaurants = Array.isArray(details.restaurants)
                    ? details.restaurants.flatMap((restaurant): LoginRestaurantOption[] => {
                        if (!restaurant || typeof restaurant !== "object") return [];
                        const option = restaurant as { restaurantId?: unknown; role?: unknown };
                        return typeof option.restaurantId === "number" &&
                            Number.isInteger(option.restaurantId) &&
                            typeof option.role === "string"
                            ? [{ restaurantId: option.restaurantId, role: option.role }]
                            : [];
                    })
                    : [];
                fieldErrors = Object.fromEntries(
                    Object.entries(details.errors ?? {}).flatMap(([field, value]) => {
                        const messages = Array.isArray(value)
                            ? value.filter((item): item is string => typeof item === "string")
                            : typeof value === "string"
                                ? [value]
                                : [];
                        return messages.length > 0 ? [[field, messages]] : [];
                    }),
                );
                const validationMessages = Object.values(fieldErrors).flat();

                message =
                    (typeof details.message === "string" && details.message) ||
                    (typeof details.error === "string" && details.error) ||
                    (typeof details.detail === "string" && details.detail) ||
                    (typeof details.title === "string" && details.title) ||
                    validationMessages.join(" ") ||
                    message;
            } else if (typeof payload === "string" && payload.trim()) {
                message = payload;
            }
        } catch (caughtError) {
            if (caughtError instanceof SyntaxError) {
                message = responseBody;
            } else {
                throw caughtError;
            }
        }
    }

    throw new ApiError(message, response.status, fieldErrors, restaurants);
}

async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
    const headers = new Headers(init.headers);
    if (!headers.has("Accept")) {
        headers.set("Accept", "application/json");
    }
    const accessToken = getStoredAccessToken();
    if (accessToken) {
        headers.set("Authorization", `Bearer ${accessToken}`);
    }

    // PlatformAdmin restaurant context is selected per request by the backend.
    const selectedRestaurantId = window.sessionStorage.getItem(selectedRestaurantStorageKey);
    if (selectedRestaurantId && !headers.has("X-Restaurant-Id")) {
        headers.set("X-Restaurant-Id", selectedRestaurantId);
    }

    const response = await fetch(`${apiBaseUrl}${path}`, {
        ...init,
        headers,
    });

    if (!response.ok) {
        if (accessToken && response.status === 401 &&
            path !== "/auth/login" && path !== "/auth/logout" &&
            getStoredAccessToken() === accessToken) {
            clearStoredAccessToken();
            window.dispatchEvent(new CustomEvent(sessionExpiredEvent));
        } else if (accessToken && response.status === 403 &&
            path !== "/auth/me" && path !== "/auth/logout") {
            window.dispatchEvent(new CustomEvent(authorizationDeniedEvent, {
                detail: { path },
            }));
        }
        return throwApiError(response);
    }

    if (response.status === 204) {
        return undefined as T;
    }

    return (await response.json()) as T;
}

export function apiGet<T>(path: string): Promise<T> {
    return apiRequest<T>(path);
}

export interface LoginResponse {
    tokenType: string;
    accessToken: string;
    expiresIn: number;
    refreshToken?: string;
}

export interface AccountResponse {
    id: string;
    userName: string;
    email: string | null;
    restaurantId?: string;
    role?: string;
}

export interface RestaurantResponse {
    id: number;
    name: string;
    address: string;
}

export interface RestaurantSettingsResponse {
    restaurantId: number;
    defaultSeatCapacity: number;
    updatedAtUtc: string;
}

export interface DiningTableResponse {
    id: number;
    restaurantId: number;
    tableNumber: number;
    seatCount: number;
    isActive: boolean;
    assignment: {
        id: number;
        staffMembershipId: number;
        userName: string | null;
        role: string;
        membershipStatus: string;
        assignedAtUtc: string;
    } | null;
}

export interface WaiterTableResponse {
    id: number;
    tableNumber: number;
    seatCount: number;
}

export function getWaiterTables(): Promise<WaiterTableResponse[]> {
    return apiGet<WaiterTableResponse[]>("/waiter/tables");
}

export interface OrderItemDetailsResponse {
    id: number;
    dishId: number;
    dishName: string;
    unitPrice: number;
    quantity: number;
    specialRequest: string | null;
    kitchenSubmissionId: number | null;
    preparationStatus: "New" | "Preparing" | "Ready";
    preparationStartedAtUtc: string | null;
    readyAtUtc: string | null;
}

export interface OrderDetailsResponse {
    id: number;
    restaurantId: number;
    diningTableId: number;
    createdByStaffMembershipId: number;
    status: "Open" | "Sent" | "Closed" | "Legacy";
    createdAtUtc: string;
    sentAtUtc: string | null;
    paidAtUtc?: string | null;
    items: OrderItemDetailsResponse[];
    seatNumber: number | null;
}

export interface OpenOrderResponse {
    orderId: number;
    seatNumber: number | null;
    status: "Open" | "Sent";
    reused: boolean;
}

export interface KitchenSubmissionResponse {
    id: number;
    orderId: number;
    diningTableId: number;
    // Public table number configured by Admin; differs from diningTableId.
    tableNumber?: number | null;
    sentAtUtc: string;
    items: OrderItemDetailsResponse[];
    seatNumber: number | null;
}

export interface WaiterNotificationResponse {
    id: number;
    orderId: number;
    orderItemId: number;
    createdAtUtc: string;
    isRead: boolean;
    readAtUtc: string | null;
    dishName: string;
    quantity: number;
    tableNumber: number;
    diningTableId: number;
    preparationStatus: "New" | "Preparing" | "Ready";
    serviceStatus: "Unserved" | "Served";
}


export interface MarkServedResponse {
    id: number;
    orderId: number;
    dishId: number;
    dishName: string;
    tableNumber: number;
    serviceStatus: "Served";
    servedAtUtc: string;
}


export function getWaiterNotifications(): Promise<WaiterNotificationResponse[]> {
    return apiRequest<WaiterNotificationResponse[]>("/waiter/notifications", {
        cache: "no-store",
    });
}


export function markWaiterItemServed(orderItemId: number): Promise<MarkServedResponse> {
    return apiRequest<MarkServedResponse>(`/waiter/orders/items/${orderItemId}/served`, {
        method: "PATCH",
    });
}


export interface WaiterOrderHistoryEntry {
    id: number;
    diningTableId: number;
    createdAtUtc: string;
    status: "Open" | "Sent" | "Closed" | "Legacy";
    // Older orders can have no seat; the API omits null seat numbers.
    seatNumber?: number | null;
}

export function getWaiterOrderHistory(): Promise<WaiterOrderHistoryEntry[]> {
    return apiRequest<WaiterOrderHistoryEntry[]>("/waiter/orders", {
        cache: "no-store",
    });
}

export interface SendOrderResponse {
    orderId: number;
    status: "Sent";
    alreadySent: boolean;
    submissionId: number | null;
    // This is the kitchen-only view. Reload /details for the editable order.
    kitchenOrder: OrderDetailsResponse;
}

export function openOrCreateOrder(
    tableId: number,
    seatNumber?: number | null,
): Promise<OpenOrderResponse> {
    const path = typeof seatNumber === "number"
        ? `/waiter/tables/${tableId}/seats/${seatNumber}/orders/open`
        : `/waiter/tables/${tableId}/orders/open`;
    return apiRequest<OpenOrderResponse>(path, {
        method: "POST",
    });
}

export function getOrderDetails(orderId: number): Promise<OrderDetailsResponse> {
    return apiRequest<OrderDetailsResponse>(`/waiter/orders/${orderId}/details`, {
        cache: "no-store",
    });
}

export function addOrderItem(
    orderId: number,
    dishId: number,
    quantity: number,
    specialRequest: string,
    reason: string,
): Promise<{ orderId: number; itemId: number }> {
    return apiSend(`/waiter/orders/${orderId}/items`, "POST", {
        dishId,
        quantity,
        specialRequest,
        reason,
    });
}

export function updateOrderItem(
    orderId: number,
    itemId: number,
    quantity: number,
    specialRequest: string,
    reason: string,
): Promise<{ orderId: number; itemId: number; updated: boolean }> {
    return apiSend(`/waiter/orders/${orderId}/items/${itemId}`, "PUT", {
        quantity,
        specialRequest,
        reason,
    });
}

export function removeOrderItem(orderId: number, itemId: number, reason: string): Promise<void> {
    return apiSend(`/waiter/orders/${orderId}/items/${itemId}`, "DELETE", {
        reason,
    });
}

export interface CancelOrderResponse {
    orderId: number;
    status: "Cancelled";
}

export function cancelWaiterOrder(
    orderId: number,
    confirm: boolean,
    reason: string,
): Promise<CancelOrderResponse> {
    return apiSend<CancelOrderResponse>(`/waiter/orders/${orderId}/cancel`, "POST", {
        confirm,
        reason,
    });
}

export function sendOrder(orderId: number): Promise<SendOrderResponse> {
    return apiRequest<SendOrderResponse>(`/waiter/orders/${orderId}/send`, {
        method: "POST",
    });
}

export function getKitchenOrders(): Promise<KitchenSubmissionResponse[]> {
    return apiRequest<KitchenSubmissionResponse[]>("/chef/orders/submissions", {
        cache: "no-store",
    });
}

export interface KitchenItemStatusUpdateResponse {
    id: number;
    orderId: number;
    dishId: number;
    status: "Preparing" | "Ready";
    preparationStartedAtUtc: string | null;
    readyAtUtc: string | null;
}

export function updateKitchenItemStatus(
    orderItemId: number,
    status: "Preparing" | "Ready",
): Promise<KitchenItemStatusUpdateResponse> {
    return apiRequest<KitchenItemStatusUpdateResponse>(
        `/chef/orders/items/${orderItemId}/status`,
        {
            method: "PATCH",
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({ status }),
        },
    );
}

export function getWaiterOrderSubmissions(orderId: number): Promise<KitchenSubmissionResponse[]> {
    return apiRequest<KitchenSubmissionResponse[]>(`/waiter/orders/${orderId}/submissions`, {
        cache: "no-store",
    });
}

export interface StaffMemberResponse {
    id: number;
    restaurantId: number;
    userId: string;
    userName: string;
    email: string | null;
    role: string;
    status: number | string;
}

export interface StaffInvitationResponse {
    id: number;
    restaurantId: number;
    email: string;
    role: string;
    status: number | string;
    expiresAtUtc: string;
    developmentToken?: string | null;
}

export interface AcceptedInvitationResponse {
    id: number;
    restaurantId: number;
    userId: string;
    role: string;
    status: number | string;
}

export function getStoredAccessToken(): string | null {
    return window.sessionStorage.getItem(accessTokenStorageKey);
}

export function storeAccessToken(token: string): void {
    window.sessionStorage.setItem(accessTokenStorageKey, token);
}

export function clearStoredAccessToken(): void {
    window.sessionStorage.removeItem(accessTokenStorageKey);
}

export function getSelectedRestaurantId(): number | null {
    const value = window.sessionStorage.getItem(selectedRestaurantStorageKey);
    if (!value) return null;
    const id = Number(value);
    return Number.isSafeInteger(id) && id > 0 ? id : null;
}

export function clearSelectedRestaurant(): void {
    window.sessionStorage.removeItem(selectedRestaurantStorageKey);
}

export async function selectRestaurantContext(restaurantId: number): Promise<void> {
    if (!Number.isSafeInteger(restaurantId) || restaurantId <= 0) {
        throw new Error("Enter a valid positive restaurant ID.");
    }

    await apiRequest<{ restaurantId: number; message: string }>(
        `/auth/select-restaurant/${restaurantId}`,
        { method: "POST" },
    );

    window.sessionStorage.setItem(selectedRestaurantStorageKey, String(restaurantId));
}

export function logout(): Promise<void> {
    return apiRequest<void>("/auth/logout", { method: "POST" });
}

export function login(
    username: string,
    password: string,
    restaurantId?: number,
): Promise<LoginResponse> {
    return apiRequest<LoginResponse>("/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password, ...(restaurantId === undefined ? {} : { restaurantId }) }),
    });
}

export function getCurrentAccount(): Promise<AccountResponse> {
    return apiGet<AccountResponse>("/auth/me");
}

export function createRestaurant(
    name: string,
    address: string,
): Promise<RestaurantResponse> {
    return apiRequest<RestaurantResponse>("/restaurants", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, address }),
    });
}

export function getRestaurantProfile(restaurantId: number): Promise<RestaurantResponse> {
    return apiGet<RestaurantResponse>(`/restaurants/${restaurantId}/profile`);
}

export function updateRestaurantProfile(
    restaurantId: number,
    name: string,
    address: string,
): Promise<RestaurantResponse> {
    return apiRequest<RestaurantResponse>(`/restaurants/${restaurantId}/profile`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, address }),
    });
}

export interface RestaurantSettingsResponse {
    restaurantId: number;
    defaultSeatCapacity: number;
    updatedAtUtc: string;
}

export interface StaffAssignmentResponse {
    id: number;
    staffMembershipId: number;
    userName: string | null;
    role: string;
    membershipStatus: string;
    assignedAtUtc: string;
}

export interface DiningTableResponse {
    id: number;
    restaurantId: number;
    tableNumber: number;
    seatCount: number;
    isActive: boolean;
    assignment: StaffAssignmentResponse | null;
}

export function getRestaurantSettings(restaurantId: number): Promise<RestaurantSettingsResponse> {
    return apiGet<RestaurantSettingsResponse>(`/restaurants/${restaurantId}/settings`);
}

export function saveRestaurantSettings(
    restaurantId: number,
    defaultSeatCapacity: number,
    create: boolean,
): Promise<RestaurantSettingsResponse> {
    return apiSend<RestaurantSettingsResponse>(
        `/restaurants/${restaurantId}/settings`,
        create ? "POST" : "PUT",
        { defaultSeatCapacity },
    );
}

export function getDiningTables(restaurantId: number): Promise<DiningTableResponse[]> {
    return apiGet<DiningTableResponse[]>(`/restaurants/${restaurantId}/tables`);
}

export interface SaveDiningTableRequest {
    tableNumber: number;
    seatCount: number;
    isActive: boolean;
}

export function saveDiningTable(
    restaurantId: number,
    tableId: number | null,
    request: SaveDiningTableRequest,
): Promise<DiningTableResponse> {
    return apiSend<DiningTableResponse>(
        tableId === null
            ? `/restaurants/${restaurantId}/tables`
            : `/restaurants/${restaurantId}/tables/${tableId}`,
        tableId === null ? "POST" : "PUT",
        request,
    );
}

export function saveTableAssignment(
    restaurantId: number,
    tableId: number,
    staffMembershipId: number,
): Promise<DiningTableResponse> {
    return apiSend<DiningTableResponse>(
        `/restaurants/${restaurantId}/tables/${tableId}/assignment`,
        "PUT",
        { staffMembershipId },
    );
}

export function removeTableAssignment(restaurantId: number, tableId: number): Promise<void> {
    return apiRequest<void>(
        `/restaurants/${restaurantId}/tables/${tableId}/assignment`,
        { method: "DELETE" },
    );
}

export function getRestaurantStaff(restaurantId: number): Promise<StaffMemberResponse[]> {
    return apiGet<StaffMemberResponse[]>(`/restaurants/${restaurantId}/staff`);
}

export function inviteStaff(
    restaurantId: number,
    email: string,
    role: string,
): Promise<StaffInvitationResponse> {
    return apiRequest<StaffInvitationResponse>(`/restaurants/${restaurantId}/invitations`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, role }),
    });
}

export function revokeStaffInvitation(
    restaurantId: number,
    invitationId: number,
): Promise<void> {
    return apiRequest<void>(
        `/restaurants/${restaurantId}/invitations/${invitationId}/revoke`,
        { method: "PATCH" },
    );
}

export function changeStaffRole(
    restaurantId: number,
    memberId: number,
    role: string,
): Promise<StaffMemberResponse> {
    return apiRequest<StaffMemberResponse>(
        `/restaurants/${restaurantId}/staff/${memberId}/role`,
        {
            method: "PATCH",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ role }),
        },
    );
}

export function setStaffMembershipStatus(
    restaurantId: number,
    memberId: number,
    active: boolean,
): Promise<StaffMemberResponse> {
    return apiRequest<StaffMemberResponse>(
        `/restaurants/${restaurantId}/staff/${memberId}/${active ? "activate" : "revoke"}`,
        { method: "PATCH" },
    );
}

export interface AcceptInvitationRequest {
    token: string;
    username?: string;
    password?: string;
}

export function acceptInvitation(
    request: AcceptInvitationRequest,
): Promise<AcceptedInvitationResponse> {
    return apiRequest<AcceptedInvitationResponse>("/invitations/accept", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request),
    });
}


export interface DishImageUploadResponse {
  dishId: number;
  imageUrl: string;
}

export async function uploadDishImage(
  id: number,
  image: File,
): Promise<DishImageUploadResponse> {
  const formData = new FormData();
  formData.append("image", image);

  const response = await fetch(`${apiBaseUrl}/dishes/${id}/image`, {
    method: "POST",
    headers: { Accept: "application/json" },
    body: formData,
  });

  if (!response.ok) {
    return throwApiError(response);
  }

  return (await response.json()) as DishImageUploadResponse;
}

async function apiSend<T>(
    path: string,
    method: "POST" | "PUT" | "DELETE",
    body: unknown,
): Promise<T> {
    return apiRequest<T>(path, {
        method,
        headers: {
            "Content-Type": "application/json",
        },
        body: JSON.stringify(body),
    });
}

export interface MenuDishResponse {
    id: number;
    name: string;
    price: number;
    description: string | null;
    category: string;
    isAvailable: boolean;
    imageUrl: string | null;
    ingredients: string[];
}

export interface DishDetailsResponse extends MenuDishResponse {
    ingredients: string[];
}

export interface SaveDishRequest {
    name: string;
    price: number;
    description: string;
    category: string;
    isAvailable: boolean;
    ingredients: string[];
}

export interface DishAvailabilityResponse {
    dishId: number;
    isAvailable: boolean;
    changed: boolean;
}


export function getMenuDishes(): Promise<MenuDishResponse[]> {
    return apiRequest<MenuDishResponse[]>("/dishes", {
        cache: "no-store",
    });
}


export function getAdminDishes(): Promise<DishDetailsResponse[]> {
    return apiGet<DishDetailsResponse[]>("/dishes/manage");
}

export interface ChefDishResponse {
    id: number;
    name: string;
    category: string;
    isAvailable: boolean;
}

export function getChefDishes(): Promise<ChefDishResponse[]> {
    return getMenuDishes().then((dishes) => dishes.map(({ id, name, category, isAvailable }) => ({
        id,
        name,
        category,
        isAvailable,
    })));
}

export function getDishDetails(id: number): Promise<DishDetailsResponse> {
    return apiGet<DishDetailsResponse>(`/dishes/${id}`);
}

export function createDish(request: SaveDishRequest): Promise<DishDetailsResponse> {
    return apiSend<DishDetailsResponse>("/dishes", "POST", request);
}

export function updateDish(
    id: number,
    request: SaveDishRequest,
): Promise<DishDetailsResponse> {
    return apiSend<DishDetailsResponse>(`/dishes/${id}`, "PUT", request);
}

export function updateDishAvailability(
    id: number,
    isAvailable: boolean,
): Promise<DishAvailabilityResponse> {
    return apiRequest<DishAvailabilityResponse>(`/dishes/${id}/availability`, {
        method: "PATCH",
        headers: {
            "Content-Type": "application/json",
        },
        body: JSON.stringify({ isAvailable }),
    });
}

export interface ApiHealth {
    status: string;
}

export function getApiHealth(): Promise<ApiHealth> {
    return apiGet<ApiHealth>("/health");
}

// Restaurant-scoped history endpoints. The backend permits Administrator
// access to all orders and waiter access only to their own/assigned orders.
export interface OrderHistorySummaryEntry {
    id: number;
    diningTableId: number;
    tableNumber: number;
    seatNumber: number | null;
    status: string;
    createdByStaffMembershipId: number;
    createdByStaffName?: string | null;
    createdAtUtc: string;
    sentAtUtc: string | null;
    paidAtUtc: string | null;
    itemCount: number;
    totalQuantity: number;
    totalAmount: number | null;
    currencyCode: string | null;
    paymentStatus: string;
}

export interface OrderHistorySummaryResponse {
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    items: OrderHistorySummaryEntry[];
}

export interface OrderHistoryItemResponse {
    id: number;
    dishId: number;
    dishName: string;
    quantity: number;
    unitPriceSnapshot: number;
    lineTotal: number;
    specialRequest: string | null;
    kitchenSubmissionId: number | null;
    preparationStatus: string;
    serviceStatus: "Unserved" | "Served";
    servedAtUtc: string | null;
}

export interface OrderHistoryEventResponse {
    id: number;
    eventType: string;
    occurredAtUtc: string;
    actorStaffMembershipId: number | null;
    orderItemId: number | null;
    beforeValueJson: string | null;
    afterValueJson: string | null;
    reason: string | null;
    source: string;
}

export interface OrderHistoryDetailsResponse {
    id: number;
    restaurantId: number;
    diningTableId: number;
    tableNumber: number;
    seatNumber: number | null;
    status: string;
    createdByStaffMembershipId: number;
    createdByStaffName?: string | null;
    createdAtUtc: string;
    sentAtUtc: string | null;
    paidAtUtc: string | null;
    items: OrderHistoryItemResponse[];
    bill: {
        id: number;
        currencyCode: string;
        totalAmount: number;
        paidAmount: number;
        balance: number;
        paymentStatus: string;
    } | null;
    timeline: OrderHistoryEventResponse[];
}

export function getOrderHistoryPage(filters: {
    page?: number; pageSize?: number; tableNumber?: number; status?: string; search?: string;
    createdByStaffMembershipId?: number;
} = {}): Promise<OrderHistorySummaryResponse> {
    const query = new URLSearchParams();
    if (filters.page) query.set("page", String(filters.page));
    if (filters.pageSize) query.set("pageSize", String(filters.pageSize));
    if (filters.tableNumber) query.set("tableNumber", String(filters.tableNumber));
    if (filters.status) query.set("status", filters.status);
    if (filters.search) query.set("search", filters.search);
    if (filters.createdByStaffMembershipId) query.set("createdByStaffMembershipId", String(filters.createdByStaffMembershipId));
    return apiRequest<OrderHistorySummaryResponse>(`/order-history?${query.toString()}`, { cache: "no-store" });
}

export function getOrderHistoryDetails(orderId: number): Promise<OrderHistoryDetailsResponse> {
    return apiRequest<OrderHistoryDetailsResponse>(`/order-history/${orderId}`, { cache: "no-store" });
}

// Waiter billing and payment API contracts. These DTOs mirror
// backend/Restaurant.Api/Dtos/Billing/BillingDtos.cs.
export type BillPaymentMethod = "Cash" | "Card" | "Other";

export interface BillLineResponse {
    orderItemId: number;
    dishName: string;
    quantity: number;
    unitPrice: number;
    lineTotal: number;
    specialRequest: string | null;
}

export interface BillPaymentResponse {
    id: number;
    amount: number;
    method: BillPaymentMethod;
    externalReference: string | null;
    recordedAtUtc: string;
}

export interface BillDetailsResponse {
    id: number;
    orderId: number;
    diningTableId: number;
    currencyCode: string;
    subtotalAmount: number;
    taxAmount: number;
    discountAmount: number;
    totalAmount: number;
    amountPaid: number;
    balanceDue: number;
    paymentStatus: "Unpaid" | "PartiallyPaid" | "Paid";
    createdAtUtc: string;
    items: BillLineResponse[];
    payments: BillPaymentResponse[];
}

export interface RecordWaiterPaymentResponse {
    alreadyRecorded?: boolean;
    payment: BillPaymentResponse;
    bill: BillDetailsResponse;
}

/** Creates a bill snapshot, or returns the existing bill if one is already present. */
export async function createWaiterBill(
    orderId: number,
    currencyCode: string,
): Promise<BillDetailsResponse> {
    const response = await apiSend<BillDetailsResponse | {
        alreadyExists?: boolean;
        bill?: BillDetailsResponse;
    }>(`/waiter/orders/${orderId}/bill`, "POST", { currencyCode });

    const bill = "bill" in response && response.bill
        ? response.bill
        : response as BillDetailsResponse;

    if (!bill || typeof bill.id !== "number" || bill.orderId !== orderId) {
        throw new Error("The billing API returned an invalid bill response.");
    }

    return bill;
}

/** Retrieves the persisted bill snapshot and payment history for an order. */
export function getWaiterBill(orderId: number): Promise<BillDetailsResponse> {
    return apiRequest<BillDetailsResponse>(`/waiter/orders/${orderId}/bill`, {
        cache: "no-store",
    });
}

/** Records a payment using the backend-required idempotency reference. */
export function recordWaiterPayment(
    billId: number,
    amount: number,
    method: BillPaymentMethod,
    externalReference: string,
): Promise<RecordWaiterPaymentResponse> {
    return apiSend<RecordWaiterPaymentResponse>(`/waiter/bills/${billId}/payments`, "POST", {
        amount,
        method,
        externalReference,
    });
}

