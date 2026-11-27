import {
  Bar, BarChart, CartesianGrid, Legend, Line, LineChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import type { ZoneDetail, ZonePointStats, ZoneType } from '../../api/types'
import { formatCount, formatDuration } from '../../lib/format'

// Paire validée (daltonisme et contraste) sur fond blanc.
const BLUE = '#3d8fd1'
const GREEN = '#0b6e4c'
const AXIS = '#5b6d66'

interface Series {
  key: Exclude<keyof ZonePointStats, 'start'>
  name: string
  color: string
}

const VISITS: Record<ZoneType, string> = {
  Shelf: 'Passages',
  Passage: 'Passages',
  Queue: 'Clients servis',
  CountingLine: 'Entrées',
  Restricted: 'Présences détectées',
  PrivacyMask: 'Passages',
}

const ACTIVITY: Partial<Record<ZoneType, Series[]>> = {
  Shelf: [{ key: 'stops', name: 'Arrêts (> 5 s)', color: BLUE }, { key: 'pickups', name: 'Prises en main', color: GREEN }],
  Passage: [{ key: 'visits', name: 'Passages', color: BLUE }, { key: 'stops', name: 'Arrêts (> 5 s)', color: GREEN }],
}

const percentValue = (v: number) => `${formatCount(v)} %`

export default function ZoneCharts({ detail, timeZone }: { detail: ZoneDetail; timeZone: string }) {
  const daily = detail.bucketMinutes >= 1440
  const label = (iso: string) => new Date(iso).toLocaleString('fr-FR', daily
    ? { day: '2-digit', month: '2-digit', timeZone }
    : { hour: '2-digit', minute: '2-digit', timeZone })
  const activity = ACTIVITY[detail.type] ?? [{ key: 'visits' as const, name: VISITS[detail.type], color: BLUE }]
  const lines: { title: string; series: Series[]; format: (v: number) => string } | null =
    detail.type === 'Shelf' ? {
      title: 'Remplissage',
      series: [{ key: 'level', name: 'Moyen', color: BLUE }, { key: 'minLevel', name: 'Minimum', color: GREEN }],
      format: percentValue,
    } : detail.type === 'Queue' ? {
      title: 'Attente moyenne',
      series: [{ key: 'waitSeconds', name: 'Attente', color: BLUE }],
      format: formatDuration,
    } : null
  const columns = [...(lines?.series ?? []), ...activity]
  const step = daily ? 'par jour' : detail.bucketMinutes === 60 ? 'par heure' : `par tranche de ${detail.bucketMinutes} min`

  const legend = (series: Series[]) => series.length > 1 && (
    <Legend iconType="square" itemSorter={null} formatter={(name) => <span className="text-sm text-ink">{name}</span>} />
  )

  return (
    <div>
      <div className={`grid gap-6 ${lines ? 'xl:grid-cols-2' : ''}`}>
        {lines && (
          <figure className="min-w-0">
            <figcaption className="mb-2 text-sm font-semibold">{lines.title} {step}</figcaption>
            <div className="h-64">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={detail.points} margin={{ top: 4, right: 8, left: -8, bottom: 0 }}>
                  <CartesianGrid vertical={false} stroke="#e1e8e5" />
                  <XAxis dataKey="start" tickFormatter={label} tickLine={false} stroke={AXIS} fontSize={12} minTickGap={16} />
                  <YAxis tickLine={false} axisLine={false} stroke={AXIS} fontSize={12}
                    domain={detail.type === 'Shelf' ? [0, 100] : [0, 'auto']} />
                  <Tooltip labelFormatter={(v) => label(String(v))} formatter={(v) => lines.format(Number(v))} />
                  {legend(lines.series)}
                  {detail.fillThreshold !== null && (
                    <ReferenceLine y={detail.fillThreshold} stroke="#c9541a" strokeDasharray="5 4"
                      label={{ value: `Seuil ${formatCount(detail.fillThreshold)} %`, position: 'insideBottomRight', fill: '#c9541a', fontSize: 12 }} />
                  )}
                  {lines.series.map((s) => (
                    <Line key={s.key} dataKey={s.key} name={s.name} stroke={s.color} strokeWidth={2} isAnimationActive={false}
                      dot={detail.points.length <= 14 ? { r: 3, fill: s.color } : false} connectNulls={false} />
                  ))}
                </LineChart>
              </ResponsiveContainer>
            </div>
          </figure>
        )}
        <figure className="min-w-0">
          <figcaption className="mb-2 text-sm font-semibold">
            {activity.length > 1 ? 'Activité' : activity[0].name} {step}
          </figcaption>
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={detail.points} barGap={2} margin={{ top: 4, right: 8, left: -8, bottom: 0 }}>
                <CartesianGrid vertical={false} stroke="#e1e8e5" />
                <XAxis dataKey="start" tickFormatter={label} tickLine={false} stroke={AXIS} fontSize={12} minTickGap={16} />
                <YAxis tickLine={false} axisLine={false} stroke={AXIS} fontSize={12} allowDecimals={false} />
                <Tooltip cursor={{ fill: '#f3f6f5' }} labelFormatter={(v) => label(String(v))}
                  formatter={(v) => formatCount(Number(v))} />
                {legend(activity)}
                {activity.map((s) => (
                  <Bar key={s.key} dataKey={s.key} name={s.name} fill={s.color} radius={[4, 4, 0, 0]} maxBarSize={18}
                    isAnimationActive={false} />
                ))}
              </BarChart>
            </ResponsiveContainer>
          </div>
        </figure>
      </div>
      <details className="mt-3">
        <summary className="cursor-pointer text-sm text-muted">Voir les valeurs</summary>
        <div className="mt-2 max-h-64 overflow-auto">
          <table className="w-full text-sm tabular-nums">
            <thead>
              <tr className="border-b border-line text-left text-xs text-muted">
                <th className="py-1.5 pr-3 font-medium">Début</th>
                {columns.map((c) => <th key={c.key} className="py-1.5 pr-3 font-medium">{c.name}</th>)}
              </tr>
            </thead>
            <tbody>
              {detail.points.map((point) => (
                <tr key={point.start} className="border-b border-line last:border-0">
                  <td className="py-1.5 pr-3">{label(point.start)}</td>
                  {columns.map((c) => {
                    const value = point[c.key]
                    const format = lines?.series.some((s) => s.key === c.key) ? lines.format : formatCount
                    return <td key={c.key} className="pr-3">{value === null ? '—' : format(value)}</td>
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  )
}
