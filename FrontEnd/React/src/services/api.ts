export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

async function throwApiError(response: Response): Promise<never> {
  const responseBody = await response.text();
  let message = `API request failed with status ${response.status}.`;

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
        const validationMessages = details.errors
          ? Object.values(details.errors).flatMap((value) =>
              Array.isArray(value)
                ? value.filter(
                    (item): item is string => typeof item === "string",
                  )
                : [],
            )
          : [];
        message =
            (typeof details.message === "string" && details.message) ||
            (typeof details.error === "string" && details.error) ||
            (typeof details.detail === "string" && details.detail) ||
            (typeof details.title === "string" && details.title) ||
            validationMessages.join(" ") ||
            message;
      }
    } catch (caughtError) {
      if (caughtError instanceof SyntaxError) {
        message = responseBody;
      } else {
              Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : [],
            )
          : [];
        message =
          (typeof details.error === "string" && details.error) ||
          (typeof details.detail === "string" && details.detail) ||
          (typeof details.title === "string" && details.title) ||
          validationMessages.join(" ") ||
          message;
      }
    } catch (caughtError) {
      if (!(caughtError instanceof SyntaxError)) {
        throw caughtError;
      }
    }
  }

  throw new ApiError(message, response.status);
}

async function apiRequest<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
export async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    return throwApiError(response);
  }

  return (await response.json()) as T;
}

export function apiGet<T>(path: string): Promise<T> {
  return apiRequest<T>(path, {
    headers: { Accept: "application/json" },
  });
}

export function apiPost<T>(path: string, body: unknown): Promise<T> {
  return apiRequest<T>(path, {
    method: "POST",
    headers: {
      Accept: "application/json",
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

export interface OrderItemResponse {
  dishId: number;
  dishName: string;
  unitPrice: number;
  quantity: number;
}

export interface OrderResponse {
  id: number;
  createdAtUtc: string;
  items: OrderItemResponse[];
}

export function createOrder(
  dishId: number,
  quantity = 1,
): Promise<OrderResponse> {
  return apiPost<OrderResponse>("/orders", { dishId, quantity });
}

export function getOrder(id: number): Promise<OrderResponse> {
  return apiGet<OrderResponse>(`/orders/${id}`);
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

async function apiSend<T>(
  path: string,
  method: "POST" | "PUT",
  body: unknown,
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    return throwApiError(response);
  }

  return (await response.json()) as T;
}

export interface ApiHealth {
  status: string;
}

export function getApiHealth(): Promise<ApiHealth> {
  return apiGet<ApiHealth>("/health");
}
