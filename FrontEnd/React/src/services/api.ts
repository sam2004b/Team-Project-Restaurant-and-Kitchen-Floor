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
          error?: unknown;
          detail?: unknown;
          title?: unknown;
          errors?: Record<string, unknown>;
        };
        const validationMessages = details.errors
          ? Object.values(details.errors).flatMap((value) =>
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

export async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: { Accept: "application/json" },
  });

  if (!response.ok) {
    return throwApiError(response);
  }

  return (await response.json()) as T;
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
