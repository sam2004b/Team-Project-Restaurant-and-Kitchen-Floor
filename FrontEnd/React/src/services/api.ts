export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

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

    const response = await fetch(`${apiBaseUrl}${path}`, {
        ...init,
        headers,
    });

    if (!response.ok) {
        return throwApiError(response);
    }

    return (await response.json()) as T;
}

export function apiGet<T>(path: string): Promise<T> {
    return apiRequest<T>(path);
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

export function getMenuDishes(): Promise<MenuDishResponse[]> {
    return apiGet<MenuDishResponse[]>("/dishes");
}

export function getAdminDishes(): Promise<DishDetailsResponse[]> {
    return apiGet<DishDetailsResponse[]>("/dishes/manage");
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

export interface ApiHealth {
    status: string;
}

export function getApiHealth(): Promise<ApiHealth> {
    return apiGet<ApiHealth>("/health");
}
