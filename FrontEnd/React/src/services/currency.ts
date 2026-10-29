export enum Currency {
  RUB = "RUB",
}

const rubleFormatter = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: Currency.RUB,
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

export function formatPrice(price: number): string {
  return rubleFormatter.format(price);
}
