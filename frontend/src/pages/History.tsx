import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, Pause, Play } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Bar, BarChart, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '../api/client'
import type { Alert, DayReplay, Pharmacy } from '../api/types'
import { Stat } from '../components/DataTable'
import PlanGrid from '../components/PlanGrid'
import { Button, Card, Modal, QueryState } from '../components/ui'
import AlertCurve from '../features/alerts/AlertCurve'
import { Thumbnail } from '../features/alerts/AlertActions'
import { STATUS_LABELS } from '../features/alerts/shared'
import { formatCount, formatDuration, formatTime } from '../lib/format'

const SPEEDS = [{ label: 'Lent', ms: 1000 }, { label: 'Normal', ms: 400 }, { label: 'Rapide', ms: 150 }]

const addDays = (date: string, days: number) => {
  const next = new Date(`${date}T00:00:00Z`)
  next.setUTCDate(next.getUTCDate() + days)
  return next.toISOString().slice(0, 10)
}

export default function History({ pharmacy }: { pharmacy: Pharmacy }) {
  // Vide : le serveur choisit « aujourd'hui » selon le fuseau de l'officine.
  const [date, setDate] = useState('')
  const day = useQuery({
    queryKey: ['history', pharmacy.id, date],
    queryFn: () => api.historyDay(pharmacy.id, date),
    placeholderData: keepPreviousData,
  })
  const data = day.data
  if (!data) return <Card><QueryState error={day.error} /></Card>

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <Button aria-label="Jour précédent" disabled={data.date <= data.firstDay} onClick={() => setDate(addDays(data.date, -1))}>
          <ChevronLeft size={16} />
        </Button>
        <input type="date" aria-label="Jour à rejouer" className="field !w-auto" min={data.firstDay} max={data.today}
          value={date || data.date} onChange={(e) => e.target.value && setDate(e.target.value)} />
        <Button aria-label="Jour suivant" disabled={data.date >= data.today} onClick={() => setDate(addDays(data.date, 1))}>
          <ChevronRight size={16} />
        </Button>
        <p className="text-sm text-muted">
          Historique conservé depuis le {new Date(`${data.firstDay}T12:00:00Z`).toLocaleDateString('fr-FR')}.
          La période du haut de l'écran ne s'applique pas ici.
        </p>
      </div>
      {day.error && <p role="alert" className="text-sm text-danger">{day.error.message}</p>}
      {data.frames.length === 0 ? (
        <Card><p className="py-8 text-center text-sm text-muted">Aucune mesure ce jour-là.</p></Card>
      ) : <Replay key={`${pharmacy.id}-${data.date}`} data={data} timeZone={pharmacy.timeZone} />}
    </div>
  )
}

function Replay({ data, timeZone }: { data: DayReplay; timeZone: string }) {
  const last = data.frames.length - 1
  const [position, setPosition] = useState(0)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(SPEEDS[1].ms)
  const [opened, setOpened] = useState<Alert | null>(null)
  const index = Math.min(position, last)
  const running = playing && index < last

  useEffect(() => {
    if (!running) return
    const timer = window.setTimeout(() => setPosition(index + 1), speed)
    return () => window.clearTimeout(timer)
  }, [running, index, speed])

  const frame = data.frames[index]
  const end = new Date(frame.start).getTime() + data.stepMinutes * 60_000
  const time = (iso: string) => formatTime(iso, timeZone)
  const upTo = data.frames.slice(0, index + 1)
  const total = (key: 'entries' | 'tickets') => upTo.reduce((sum, f) => sum + f[key], 0)
  const peak = Math.max(1, ...data.frames.flatMap((f) => f.zones.map((z) => z.presence)))
  const active = data.alerts.filter((a) =>
    new Date(a.startedAt).getTime() < end && (!a.endedAt || new Date(a.endedAt).getTime() > new Date(frame.start).getTime()))
  const states = new Map(frame.zones.map((z) => [z.zoneId, z]))

  const toggle = () => {
    if (!running && index === last) setPosition(0)
    setPlaying(!running)
  }

  return (
    <>
      <Card>
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="primary" onClick={toggle}>
            {running ? <><Pause size={15} /> Pause</> : <><Play size={15} /> Lecture</>}
          </Button>
          <p className="w-32 font-title text-lg font-semibold tabular-nums" aria-live="off">
            {time(frame.start)} – {time(new Date(end).toISOString())}
          </p>
          <input type="range" aria-label="Moment de la journée" className="min-w-48 flex-1 accent-brand-dark" min={0} max={last}
            value={index} aria-valuetext={time(frame.start)}
            onChange={(e) => { setPlaying(false); setPosition(Number(e.target.value)) }} />
          <select aria-label="Vitesse de lecture" className="field !w-auto" value={speed} onChange={(e) => setSpeed(Number(e.target.value))}>
            {SPEEDS.map((s) => <option key={s.ms} value={s.ms}>{s.label}</option>)}
          </select>
        </div>
      </Card>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label="Visiteurs depuis l'ouverture" value={formatCount(total('entries'))} note={`+${frame.entries} sur la tranche`} />
        <Stat label="Tickets depuis l'ouverture" value={formatCount(total('tickets'))} note={`+${frame.tickets} sur la tranche`} />
        <Stat label="Occupation estimée" value={formatCount(frame.occupancy)} note="Entrées moins sorties" />
        <Stat label="Attente au comptoir" value={frame.waitSeconds === null ? '—' : formatDuration(frame.waitSeconds)}
          note="Moyenne des clients servis sur la tranche" />
      </div>

      <div className="grid gap-4 xl:grid-cols-3">
        <Card title="Plan à cet instant" className="xl:col-span-2">
          <PlanGrid rowHeight="3rem" cells={data.zones.map((zone) => {
            const state = states.get(zone.id)
            const level = state?.level ?? null
            const low = level !== null && zone.threshold !== null && level < zone.threshold
            const fill = level === null ? '' : `${Math.round(level)} %${low ? ' · rupture' : ''}`
            return {
              id: zone.id, x: zone.x, y: zone.y, w: zone.w, h: zone.h,
              intensity: Math.sqrt((state?.presence ?? 0) / peak),
              label: `${zone.name} : ${formatDuration(state?.presence ?? 0)} de présence${fill && `, remplissage ${fill}`}`,
              content: (
                <>
                  <span>{zone.name}</span>
                  {fill && <span className={`mt-0.5 rounded px-1 tabular-nums ${low ? 'bg-danger text-white' : 'bg-white/80 text-ink'}`}>{fill}</span>}
                </>
              ),
            }
          })} />
        </Card>
        <Card title={`Alertes actives (${active.length})`}>
          {active.length === 0 ? <p className="text-sm text-muted">Aucune alerte à ce moment.</p> : (
            <ul className="space-y-2">
              {active.map((alert) => (
                <li key={alert.id} className={`rounded-btn border border-line border-l-4 p-2.5 ${alert.severity === 'High' ? 'border-l-danger' : 'border-l-warn'}`}>
                  <p className="text-sm font-semibold">{alert.title}</p>
                  <p className="text-xs text-muted">
                    Depuis {time(alert.startedAt)}{alert.endedAt && `, close à ${time(alert.endedAt)}`}
                  </p>
                  <Button className="mt-2" onClick={() => setOpened(alert)}>Image et courbe</Button>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <Card title="Fréquentation de la journée">
        <div className="h-48">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data.frames} margin={{ top: 4, right: 8, left: -16, bottom: 0 }}
              onClick={(e) => {
                if (e.activeTooltipIndex != null) { setPlaying(false); setPosition(Number(e.activeTooltipIndex)) }
              }}>
              <XAxis dataKey="start" tickFormatter={time} tickLine={false} stroke="#5b6d66" fontSize={12} minTickGap={24} />
              <YAxis tickLine={false} axisLine={false} stroke="#5b6d66" fontSize={12} allowDecimals={false} />
              <Tooltip cursor={{ fill: '#f3f6f5' }} labelFormatter={(v) => time(String(v))} />
              <ReferenceLine x={frame.start} stroke="#0f1a17" strokeWidth={2} />
              <Bar dataKey="entries" name="Entrées" fill="#3d8fd1" radius={[3, 3, 0, 0]} isAnimationActive={false} />
            </BarChart>
          </ResponsiveContainer>
        </div>
        <p className="mt-1 text-xs text-muted">Entrées par quart d'heure. Cliquez une barre pour vous y placer.</p>
      </Card>

      {opened && (
        <Modal title={opened.title} onClose={() => setOpened(null)}>
          {opened.cameraId && opened.kind !== 'CameraOffline' && <Thumbnail alertId={opened.id} />}
          <p className="mt-3 text-sm">{opened.detail} · {STATUS_LABELS[opened.status]}</p>
          <AlertCurve alert={opened} timeZone={timeZone} />
        </Modal>
      )}
    </>
  )
}
