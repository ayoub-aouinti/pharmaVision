import type { UseQueryResult } from '@tanstack/react-query'
import { ArrowDownRight, ArrowUpRight, Minus } from 'lucide-react'
import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { HourPoint, Kpi, KpiSummary, Period } from '../../api/types'
import { Card, Info, QueryState, Segmented } from '../../components/ui'
import { explain, GLOSSARY, type Term } from '../../lib/glossary'
import { formatKpi, formatPercent } from '../../lib/format'

const REFERENCE: Record<Period, string> = {
  live: "que l'heure d'avant",
  today: "qu'hier à la même heure",
  '7d': 'que les 7 jours d’avant',
  '30d': 'que les 30 jours d’avant',
}

type Series = 'visitors' | 'tickets' | 'conversion'

const SERIES: Record<Series, { label: string; color: string; value: (h: HourPoint) => number; format: (v: number) => string }> = {
  visitors: { label: 'Visiteurs', color: '#3d8fd1', value: (h) => h.visitors, format: (v) => v.toLocaleString('fr-FR') },
  tickets: { label: 'Tickets de caisse', color: '#0b6e4c', value: (h) => h.tickets, format: (v) => v.toLocaleString('fr-FR') },
  conversion: {
    label: 'Visiteurs qui achètent', color: '#12a06e',
    // Un ticket peut tomber dans l'heure qui suit l'entrée : le rapport est plafonné.
    value: (h) => (h.visitors ? Math.min(1, h.tickets / h.visitors) : 0), format: formatPercent,
  },
}

/** Les chiffres de la période et la journée heure par heure. */
export default function Activity({ kpis, hourly, period }: {
  kpis: UseQueryResult<KpiSummary>
  hourly: UseQueryResult<HourPoint[]>
  period: Period
}) {
  const [series, setSeries] = useState<Series>('visitors')
  const shown = SERIES[series]
  const average = period === '7d' || period === '30d'

  return (
    <Card title="Comment se passe l'activité ?">
      {!kpis.data ? <QueryState error={kpis.error} /> : (
        <dl className="grid grid-cols-2 gap-x-6 gap-y-5 md:grid-cols-3 xl:grid-cols-5">
          {kpis.data.items.map((kpi) => <Figure key={kpi.key} kpi={kpi} reference={REFERENCE[period]} />)}
        </dl>
      )}
      <div className="mt-6 border-t border-line pt-4">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-base font-semibold">
            {average ? 'Heure par heure, en moyenne par jour' : "Aujourd'hui, heure par heure"}
          </h3>
          <Segmented label="Courbe affichée" value={series} onChange={setSeries}
            options={(Object.keys(SERIES) as Series[]).map((value) => ({ value, label: SERIES[value].label }))} />
        </div>
        {!hourly.data ? <QueryState error={hourly.error} /> : (
          <div className="h-60">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={hourly.data.map((h) => ({ hour: h.hour, amount: shown.value(h) }))}
                margin={{ top: 4, right: 4, left: -8, bottom: 0 }}>
                <CartesianGrid vertical={false} stroke="#dbe5e1" />
                <XAxis dataKey="hour" tickFormatter={(h) => `${h} h`} tickLine={false} axisLine={false} stroke="#53665f" fontSize={12} />
                <YAxis tickLine={false} axisLine={false} stroke="#53665f" fontSize={12}
                  tickFormatter={(v) => (series === 'conversion' ? formatPercent(v) : String(v))} />
                <Tooltip cursor={{ fill: '#eef3f1' }} labelFormatter={(h) => `Entre ${h} h et ${Number(h) + 1} h`}
                  formatter={(v) => [shown.format(Number(v)), shown.label]} />
                <Bar dataKey="amount" fill={shown.color} radius={[6, 6, 0, 0]} maxBarSize={34} animationDuration={400} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        )}
      </div>
    </Card>
  )
}

const TERMS: Record<string, Term> = {
  visitors: 'visitors', conversion: 'conversion', wait: 'wait', ruptures: 'ruptures', engagement: 'engagement',
}

function Figure({ kpi, reference }: { kpi: Kpi; reference: string }) {
  const term = TERMS[kpi.key]
  // Le serveur précise la période pour les visiteurs ; les autres prennent le mot simple.
  const label = kpi.key === 'visitors' || !term ? kpi.label : GLOSSARY[term].word
  const flat = kpi.trend === null || Math.abs(kpi.trend) < 0.005
  const good = !flat && (kpi.trend! > 0) === kpi.higherIsBetter
  const Icon = flat ? Minus : kpi.trend! > 0 ? ArrowUpRight : ArrowDownRight

  return (
    <div className="min-w-0">
      <dt className="flex items-start gap-1 text-sm text-muted">
        <span>{label}</span>
        {term && <Info text={explain(term)} />}
      </dt>
      <dd className="mt-1 font-title text-4xl font-semibold tabular-nums">{formatKpi(kpi)}</dd>
      <dd className="mt-1 text-xs text-muted">
        {kpi.trend === null ? 'Pas encore de comparaison' : (
          <>
            <span className={`inline-flex items-center gap-0.5 font-bold ${flat ? '' : good ? 'text-brand-dark' : 'text-danger'}`}>
              <Icon size={14} />{kpi.trend > 0 ? '+' : ''}{formatPercent(kpi.trend)}
            </span>{' '}
            {reference}
          </>
        )}
      </dd>
    </div>
  )
}
