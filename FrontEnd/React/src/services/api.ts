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

async function apiRequest<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    const body = await response.text();
    let message = `API request failed with status ${response.status}.`;

    if (body) {
      try {
        const errorBody: unknown = JSON.parse(body);
        if (
          typeof errorBody === "object" &&
          errorBody !== null &&
          "message" in errorBody &&
          typeof errorBody.message === "string"
        ) {
          message = errorBody.message;
        } else if (
          typeof errorBody === "object" &&
          errorBody !== null &&
          "error" in errorBody &&
          typeof errorBody.error === "string"
        ) {
          message = errorBody.error;
        }
      } catch {
        message = body;
      }
    }

    throw new ApiError(message, response.status);
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
}

export interface ApiHealth {
  status: string;
}

export function getApiHealth(): Promise<ApiHealth> {
  return apiGet<ApiHealth>("/health");
}
