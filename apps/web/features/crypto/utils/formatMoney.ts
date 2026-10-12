export default function formatMoney(value: number | null) {
  return value == null
    ? '—'
    : `฿${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}
