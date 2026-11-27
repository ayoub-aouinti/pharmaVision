import type { UseQueryResult } from '@tanstack/react-query'
import { AlertTriangle, CircleCheck, CircleHelp, CircleX } from 'lucide-react'
import type { ShelfRow } from '../../api/types'
import { Card, QueryState } from '../../components/ui'
import { formatCount, formatDuration, formatPercent } from '../../lib/format'

const STATES = {
  Conforme: { label: 'Conforme', icon: CircleCheck, badge: 'bg-brand/12 text-brand-dark', bar: 'bg-brand' },
  StockBas: { label: 'Stock bas', icon: AlertTriangle, badge: 'bg-warn/15 text-[#8a4310]', bar: 'bg-warn' },
  Rupture: { label: 'Rupture', icon: CircleX, badge: 'bg-danger text-white', bar: 'bg-danger' },
  Inconnu: { label: 'Sans mesure', icon: CircleHelp, badge: 'bg-bg text-muted', bar: 'bg-line' },
}

const HEADERS = [
  ['Linéaire', ''],
  ['Remplissage', ''],
  ['Arrêts', 'Arrêts clients de plus de 5 secondes'],
  ['Engagement', 'Arrêts ÷ passages devant le linéaire'],
  ['Prise en main', 'Prises en main ÷ arrêts'],
  ['Transformation', 'Unités vendues de la famille ÷ prises en main'],
  ['Sous le seuil', 'Temps cumulé en rupture sur la période'],
  ['État', ''],
]

export default function ShelfTable({ query, className }: { query: UseQueryResult<ShelfRow[]>; className?: string }) {
  return (
    <Card title="État des linéaires" className={className}>
      {!query.data ? <QueryState error={query.error} /> : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-line text-left text-xs text-muted">
                {HEADERS.map(([label, help]) => (
                  <th key={label} title={help || undefined} className="py-2 pr-2 font-medium">{label}</th>
                ))}
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {query.data.map((row) => {
                const state = STATES[row.state]
                return (
                  <tr key={row.zoneId} className="border-b border-line last:border-0">
                    <td className="py-2.5 pr-2">
                      <p className="font-medium whitespace-nowrap">{row.name}</p>
                      <p className="max-w-36 truncate text-xs text-muted" title={row.brands ?? ''}>{row.brands}</p>
                    </td>
                    <td className="pr-2">
                      <div className="flex items-center gap-2">
                        <div className="h-2 w-12 overflow-hidden rounded-full bg-bg">
                          <div className={`h-full rounded-full ${state.bar}`} style={{ width: `${row.fill ?? 0}%` }} />
                        </div>
                        <span className="w-11 text-right">{row.fill === null ? '—' : `${Math.round(row.fill)} %`}</span>
                      </div>
                    </td>
                    <td className="pr-2">{formatCount(row.stops)}</td>
                    <td className="pr-2">{formatPercent(row.engagementRate)}</td>
                    <td className="pr-2">{formatPercent(row.pickupRate)}</td>
                    <td className="pr-2">{formatPercent(row.transformation)}</td>
                    <td className="pr-2 whitespace-nowrap">
                      {row.ruptureMinutes ? formatDuration(row.ruptureMinutes * 60) : '—'}
                    </td>
                    <td>
                      <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold whitespace-nowrap ${state.badge}`}>
                        <state.icon size={13} /> {state.label}
                      </span>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          {query.data.length === 0 && <p className="py-6 text-center text-sm text-muted">Aucun linéaire configuré.</p>}
        </div>
      )}
    </Card>
  )
}
