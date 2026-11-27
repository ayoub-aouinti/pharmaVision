import type { ReactNode } from 'react'

/** Tableau défilant : la première colonne nomme la ligne, les autres sont alignées sur elle. */
export function DataTable({ headers, rows, empty }: {
  headers: (string | { label: string; help: string })[]
  rows: { key: string | number; selected?: boolean; cells: ReactNode[] }[]
  empty: string
}) {
  if (rows.length === 0) return <p className="py-6 text-center text-sm text-muted">{empty}</p>
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-line text-left text-xs text-muted">
            {headers.map((header, i) => typeof header === 'string'
              ? <th key={i} className="py-2 pr-3 font-medium">{header}</th>
              : <th key={i} title={header.help} className="py-2 pr-3 font-medium">{header.label}</th>)}
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {rows.map((row) => (
            <tr key={row.key} className={`border-b border-line last:border-0 ${row.selected ? 'bg-brand/8' : ''}`}>
              {row.cells.map((cell, i) => <td key={i} className="py-2.5 pr-3">{cell}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function Stat({ label, value, note }: { label: string; value: string; note?: ReactNode }) {
  return (
    <section className="rounded-card bg-white p-5">
      <p className="text-sm text-muted">{label}</p>
      <p className="mt-1 font-title text-3xl font-semibold">{value}</p>
      {note && <p className="mt-1 text-xs text-muted">{note}</p>}
    </section>
  )
}
