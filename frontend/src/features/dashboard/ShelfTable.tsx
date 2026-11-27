import type { UseQueryResult } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import type { ShelfRow } from '../../api/types'
import { Card, Info, QueryState } from '../../components/ui'
import { explain } from '../../lib/glossary'
import { formatCount, formatPercent } from '../../lib/format'
import { FillBar, ShelfBadge } from '../shelves/ShelfState'

const HEADERS = [
  ['Rayon', ''],
  ['Remplissage', explain('fill')],
  ['Arrêts', explain('stop')],
  ['Intérêt', explain('engagement')],
  ['Prises en main', explain('pickup')],
  ['État', ''],
]

/** Les rayons, le moins rempli en premier. */
export default function ShelfTable({ query, className }: { query: UseQueryResult<ShelfRow[]>; className?: string }) {
  const rows = [...(query.data ?? [])].sort((a, b) => (a.fill ?? 101) - (b.fill ?? 101))
  return (
    <Card title="Vos rayons" className={className}
      aside={<Link to="/zones" className="text-sm font-semibold text-brand-dark hover:underline">Voir le détail</Link>}>
      {!query.data ? <QueryState error={query.error} /> : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-line text-left text-muted">
                {HEADERS.map(([label, help]) => (
                  <th key={label} className="pr-3 pb-2 font-medium whitespace-nowrap">
                    <span className="inline-flex items-center gap-1">{label}{help && <Info text={help} />}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {rows.map((row) => (
                <tr key={row.zoneId} className="border-b border-line last:border-0">
                  <td className="py-3 pr-3">
                    <Link to={`/zones?zone=${row.zoneId}`} className="font-semibold whitespace-nowrap hover:underline">{row.name}</Link>
                    <p className="max-w-52 truncate text-xs text-muted" title={row.brands ?? ''}>{row.brands}</p>
                  </td>
                  <td className="pr-3"><FillBar fill={row.fill} state={row.state} /></td>
                  <td className="pr-3">{formatCount(row.stops)}</td>
                  <td className="pr-3">{formatPercent(row.engagementRate)}</td>
                  <td className="pr-3">{formatPercent(row.pickupRate)}</td>
                  <td><ShelfBadge state={row.state} /></td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length === 0 && <p className="py-6 text-center text-sm text-muted">Dessinez un rayon depuis l'écran Caméras.</p>}
        </div>
      )}
    </Card>
  )
}
