export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");
const accessTokenStorageKey = "restaurant.accessToken";

export class ApiError extends Error {
    constructor(
        message: string,
        readonly status: number,
        readonly fieldErrors: Record<string, string[]> = {},
    ) {
        super(message);
        this.name = "ApiError";
    }
}

async function throwApiError(response: Response): Promise<never> {
    const responseBody = await response.text();
    let message = `API request failed with status ${response.status}.`;
    let fieldErrors: Record<string, string[]> = {};

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
                };

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

    throw new ApiError(message, response.status, fieldErrors);
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

    const response = await fetch(`${apiBaseUrl}${path}`, {
        ...init,
        headers,
    });

    if (!response.ok) {
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

export function login(username: string, password: string): Promise<LoginResponse> {
    return apiRequest<LoginResponse>("/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ username, password }),
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

export function getRestaurantSettings(restaurantId: number): Promise<RestaurantSettingsResponse> {
    return apiGet<RestaurantSettingsResponse>(`/restaurants/${restaurantId}/settings`);
}

export function createRestaurantSettings(
    restaurantId: number,
    defaultSeatCapacity: number,
): Promise<RestaurantSettingsResponse> {
    return apiRequest<RestaurantSettingsResponse>(`/restaurants/${restaurantId}/settings`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ defaultSeatCapacity }),
    });
}

export function updateRestaurantSettings(
    restaurantId: number,
    defaultSeatCapacity: number,
): Promise<RestaurantSettingsResponse> {
    return apiRequest<RestaurantSettingsResponse>(`/restaurants/${restaurantId}/settings`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ defaultSeatCapacity }),
    });
}

export function getDiningTables(restaurantId: number): Promise<DiningTableResponse[]> {
    return apiGet<DiningTableResponse[]>(`/restaurants/${restaurantId}/tables`);
}

export function createDiningTable(
    restaurantId: number,
    tableNumber: number,
    seatCount: number,
    isActive: boolean,
): Promise<DiningTableResponse> {
    return apiRequest<DiningTableResponse>(`/restaurants/${restaurantId}/tables`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tableNumber, seatCount, isActive }),
    });
}

export function updateDiningTable(
    restaurantId: number,
    tableId: number,
    tableNumber: number,
    seatCount: number,
    isActive: boolean,
): Promise<DiningTableResponse> {
    return apiRequest<DiningTableResponse>(`/restaurants/${restaurantId}/tables/${tableId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ tableNumber, seatCount, isActive }),
    });
}

export function saveTableAssignment(
    restaurantId: number,
    tableId: number,
    staffMembershipId: number,
): Promise<DiningTableResponse> {
    return apiRequest<DiningTableResponse>(`/restaurants/${restaurantId}/tables/${tableId}/assignment`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ staffMembershipId }),
    });
}

export function removeTableAssignment(restaurantId: number, tableId: number): Promise<void> {
    return apiRequest<void>(`/restaurants/${restaurantId}/tables/${tableId}/assignment`, {
        method: "DELETE",
    });
}

export function getWaiterTables(): Promise<WaiterTableResponse[]> {
    return apiGet<WaiterTableResponse[]>("/waiter/tables");
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
    method: "POST" | "PUT",
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
