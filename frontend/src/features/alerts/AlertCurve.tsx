import { useQuery } from '@tanstack/react-query'
import { CartesianGrid, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '../../api/client'
import type { Alert } from '../../api/types'
import { QueryState } from '../../components/ui'
import { formatCount, formatDuration } from '../../lib/format'

/** Courbe de la zone autour de l'alerte ; les alertes sans zone (caméra) n'en ont pas. */
export default function AlertCurve({ alert, timeZone }: { alert: Alert; timeZone: string }) {
  const context = useQuery({
    queryKey: ['alerts', 'context', alert.id, alert.status],
    queryFn: () => api.alertContext(alert.id),
    enabled: alert.zoneId !== null,
  })
  if (alert.zoneId === null) return null
  const data = context.data
  if (!data) return <QueryState error={context.error} />

  const time = (iso: string) => new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', timeZone })
  const format = (v: number) => (data.unit === 'percent' ? `${formatCount(v)} %` : formatDuration(v))
  // L'axe est découpé en tranches : le début de l'alerte se place sur la sienne.
  const startedAt = new Date(alert.startedAt).getTime()
  const started = data.points.filter((p) => new Date(p.start).getTime() <= startedAt).at(-1)?.start

  return (
    <figure className="mt-4">
      <figcaption className="mb-1 text-sm font-semibold">{data.metric} autour de l'alerte</figcaption>
      {data.points.every((p) => p.value === null) ? (
        <p className="py-4 text-center text-sm text-muted">Aucune mesure enregistrée sur cet intervalle.</p>
      ) : (
        <div className="h-44">
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={data.points} margin={{ top: 8, right: 8, left: -8, bottom: 0 }}>
              <CartesianGrid vertical={false} stroke="#e1e8e5" />
              <XAxis dataKey="start" tickFormatter={time} tickLine={false} stroke="#5b6d66" fontSize={12} minTickGap={24} />
              <YAxis tickLine={false} axisLine={false} stroke="#5b6d66" fontSize={12}
                domain={data.unit === 'percent' ? [0, 100] : [0, (max: number) => Math.max(max, data.threshold ?? 0)]} />
              <Tooltip labelFormatter={(v) => time(String(v))} formatter={(v) => [format(Number(v)), data.metric]} />
              {data.threshold !== null && <ReferenceLine y={data.threshold} stroke="#c9541a" strokeDasharray="5 4" />}
              {started && <ReferenceLine x={started} stroke="#0f1a17"
                label={{ value: 'Alerte', position: 'insideTopLeft', fontSize: 12, fill: '#0f1a17' }} />}
              <Line dataKey="value" stroke="#3d8fd1" strokeWidth={2} dot={{ r: 2.5, fill: '#3d8fd1' }} connectNulls
                isAnimationActive={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      )}
      {data.threshold !== null && <p className="mt-1 text-xs text-muted">Pointillés : seuil de la règle ({format(data.threshold)}).</p>}
    </figure>
  )
}
