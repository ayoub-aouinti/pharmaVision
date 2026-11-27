import type { UseQueryResult } from '@tanstack/react-query'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { HourPoint, Period } from '../../api/types'
import { Card, QueryState } from '../../components/ui'

// Paire validée (daltonisme et contraste) sur fond blanc.
const VISITORS = '#3d8fd1'
const TICKETS = '#0b6e4c'

export default function HourlyChart({ query, period, className }: {
  query: UseQueryResult<HourPoint[]>; period: Period; className?: string
}) {
  const average = period === '7d' || period === '30d'
  return (
    <Card title={average ? 'Historique horaire (moyenne par jour)' : "Historique horaire du jour"} className={className}>
      {!query.data ? <QueryState error={query.error} /> : (
        <div className="h-72">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={query.data} barGap={2} margin={{ top: 4, right: 4, left: -16, bottom: 0 }}>
              <CartesianGrid vertical={false} stroke="#e1e8e5" />
              <XAxis dataKey="hour" tickFormatter={(h) => `${h} h`} tickLine={false} stroke="#5b6d66" fontSize={12} />
              <YAxis tickLine={false} axisLine={false} stroke="#5b6d66" fontSize={12} />
              <Tooltip cursor={{ fill: '#f3f6f5' }} labelFormatter={(h) => `${h} h – ${Number(h) + 1} h`}
                formatter={(value) => Number(value).toLocaleString('fr-FR')} />
              <Legend iconType="square" itemSorter={null} formatter={(label) => <span className="text-sm text-ink">{label}</span>} />
              <Bar dataKey="visitors" name="Visiteurs" fill={VISITORS} radius={[4, 4, 0, 0]} maxBarSize={18} isAnimationActive={false} />
              <Bar dataKey="tickets" name="Tickets de caisse" fill={TICKETS} radius={[4, 4, 0, 0]} maxBarSize={18} isAnimationActive={false} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Card>
  )
}
