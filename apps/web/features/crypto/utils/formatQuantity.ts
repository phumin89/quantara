export default function formatQuantity(value: number) {
  return value.toLocaleString("en-US", { maximumFractionDigits: 12 });
}
