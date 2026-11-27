import type { UseQueryResult } from '@tanstack/react-query'
import { ArrowDownRight, ArrowUpRight, Minus } from 'lucide-react'
import type { Kpi, KpiSummary, Period } from '../../api/types'
import { Card, QueryState } from '../../components/ui'
import { formatKpi, formatPercent } from '../../lib/format'

const REFERENCE: Record<Period, string> = {
  live: "vs l'heure précédente",
  today: 'vs hier, même heure',
  '7d': 'vs 7 jours précédents',
  '30d': 'vs 30 jours précédents',
}

export default function KpiCards({ query, period }: { query: UseQueryResult<KpiSummary>; period: Period }) {
  if (!query.data) return <Card><QueryState error={query.error} /></Card>
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-5">
      {query.data.items.map((kpi) => (
        <Card key={kpi.key}>
          <p className="text-sm text-muted">{kpi.label}</p>
          <p className="mt-1 font-title text-3xl font-semibold">{formatKpi(kpi)}</p>
          <Trend kpi={kpi} reference={REFERENCE[period]} />
        </Card>
      ))}
    </div>
  )
}

function Trend({ kpi, reference }: { kpi: Kpi; reference: string }) {
  if (kpi.trend === null) return <p className="mt-1 text-xs text-muted">Pas de référence</p>
  const flat = Math.abs(kpi.trend) < 0.005
  const good = (kpi.trend > 0) === kpi.higherIsBetter
  const Icon = flat ? Minus : kpi.trend > 0 ? ArrowUpRight : ArrowDownRight
  return (
    <p className="mt-1 flex flex-wrap items-center gap-1 text-xs text-muted">
      <span className={`flex items-center font-semibold ${flat ? '' : good ? 'text-brand-dark' : 'text-danger'}`}>
        <Icon size={14} />
        {kpi.trend > 0 ? '+' : ''}{formatPercent(kpi.trend)}
      </span>
      {reference}
    </p>
  )
}
