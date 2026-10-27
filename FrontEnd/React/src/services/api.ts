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

export async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: { Accept: "application/json" },
  });

  if (!response.ok) {
    throw new ApiError(`API request failed with status ${response.status}.`, response.status);
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

export function getMenuDishes(): Promise<MenuDishResponse[]> {
  return apiGet<MenuDishResponse[]>("/dishes");
}

export interface ApiHealth {
  status: string;
}

export function getApiHealth(): Promise<ApiHealth> {
  return apiGet<ApiHealth>("/health");
}
