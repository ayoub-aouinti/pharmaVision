import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Pencil } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import type { Period, Pharmacy, ZoneRow, ZoneType } from '../api/types'
import { DataTable, Stat } from '../components/DataTable'
import { Card, QueryState } from '../components/ui'
import { ALERT_KINDS, STATUS_LABELS } from '../features/alerts/shared'
import { FillBar, ShelfBadge } from '../features/shelves/ShelfState'
import ZoneCharts from '../features/zones/ZoneCharts'
import { formatCount, formatDuration, formatPercent, ZONE_TYPES } from '../lib/format'

export default function Zones({ pharmacy, period }: { pharmacy: Pharmacy; period: Period }) {
  const [type, setType] = useState<ZoneType | ''>('')
  const [search] = useSearchParams()
  const [selected, setSelected] = useState<number | null>(() => Number(search.get('zone')) || null)
  const zones = useQuery({
    queryKey: ['analytics', 'zones', pharmacy.id, period],
    queryFn: () => api.zoneRows(pharmacy.id, period),
    placeholderData: keepPreviousData,
  })

  if (!zones.data) return <Card><QueryState error={zones.error} /></Card>

  const visible = zones.data.filter((z) => !type || z.type === type)
  // Par défaut, le premier linéaire : c'est la zone la plus riche en mesures.
  const zone = visible.find((z) => z.zoneId === selected) ?? visible.find((z) => z.type === 'Shelf') ?? visible[0]
  const types = Object.keys(ZONE_TYPES).filter((t) => zones.data.some((z) => z.type === t)) as ZoneType[]
  const shelves = zones.data.filter((z) => z.type === 'Shelf')
  const count = (state: ZoneRow['state']) => shelves.filter((z) => z.state === state).length

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Stat label="Zones configurées" value={formatCount(zones.data.length)} note={`dont ${shelves.length} linéaire(s)`} />
        <Stat label="Linéaires en rupture" value={formatCount(count('Rupture'))} note="Au dernier relevé" />
        <Stat label="Linéaires en stock bas" value={formatCount(count('StockBas'))} note="Au dernier relevé" />
        <Stat label="Zones avec alerte en cours" value={formatCount(zones.data.filter((z) => z.activeAlerts > 0).length)} />
      </div>

      <Card title="Zones & linéaires" aside={
        <select aria-label="Type de zone" className="field !w-auto" value={type}
          onChange={(e) => setType(e.target.value as ZoneType | '')}>
          <option value="">Tous les types</option>
          {types.map((t) => <option key={t} value={t}>{ZONE_TYPES[t].label}</option>)}
        </select>
      }>
        <DataTable empty="Aucune zone. Dessinez-en depuis l'écran Caméras."
          headers={[
            'Zone', 'Caméra',
            { label: 'Passages', help: 'Entrées, passages ou clients servis selon le type de zone' },
            { label: 'Arrêts', help: 'Arrêts de plus de 5 secondes' },
            { label: 'Prises en main', help: 'Linéaires uniquement' },
            { label: 'Mesure', help: 'Remplissage actuel d’un linéaire, attente moyenne d’un comptoir' },
            'Règles actives', 'Alertes en cours',
          ]}
          rows={visible.map((z) => ({
            key: z.zoneId,
            selected: z.zoneId === zone?.zoneId,
            cells: [
              <button type="button" aria-pressed={z.zoneId === zone?.zoneId} onClick={() => setSelected(z.zoneId)}
                className="flex items-center gap-2 text-left">
                <span className="size-3 shrink-0 rounded-sm" style={{ background: ZONE_TYPES[z.type].color }} />
                <span>
                  <span className="block font-medium whitespace-nowrap underline-offset-2 hover:underline">{z.name}</span>
                  <span className="block text-xs text-muted">{z.category ?? ZONE_TYPES[z.type].label}</span>
                </span>
              </button>,
              <span className="whitespace-nowrap">
                {z.cameraName}
                {z.cameraStatus !== 'Online' && <span className="block text-xs text-danger">
                  {z.cameraStatus === 'Offline' ? 'Hors ligne' : 'Non configurée'}
                </span>}
              </span>,
              formatCount(z.visits),
              z.type === 'Shelf' || z.type === 'Passage' ? formatCount(z.stops) : '—',
              z.type === 'Shelf' ? formatCount(z.pickups) : '—',
              z.state ? <div className="flex items-center gap-2"><FillBar fill={z.fill} state={z.state} /><ShelfBadge state={z.state} /></div>
                : z.avgWaitSeconds === null ? '—' : `Attente ${formatDuration(z.avgWaitSeconds)}`,
              <span className="text-xs">{z.rules.map((r) => ALERT_KINDS[r].label).join(', ') || 'Aucune'}</span>,
              z.activeAlerts ? <span className="rounded-full bg-warn/15 px-2 py-0.5 text-xs font-semibold text-[#8a4310]">{z.activeAlerts}</span> : '—',
            ],
          }))} />
      </Card>

      {zone && <ZonePanel key={zone.zoneId} row={zone} pharmacy={pharmacy} period={period} />}
    </div>
  )
}

function ZonePanel({ row, pharmacy, period }: { row: ZoneRow; pharmacy: Pharmacy; period: Period }) {
  const detail = useQuery({
    queryKey: ['analytics', 'zone', pharmacy.id, row.zoneId, period],
    queryFn: () => api.zoneDetail(pharmacy.id, row.zoneId, period),
    placeholderData: keepPreviousData,
  })
  const isShelf = row.type === 'Shelf'
  const facts: [string, string | null][] = [
    ['Marques', row.brands],
    ['Planogramme', row.planogramRef],
    ['Facings attendus', row.facingsExpected?.toString() ?? null],
    ['Engagement', isShelf && row.visits ? formatPercent(Math.min(1, row.stops / row.visits)) : null],
    ['Prise en main', isShelf && row.stops ? formatPercent(row.pickups / row.stops) : null],
    ['Durée moyenne d’arrêt', detail.data?.avgStopSeconds ? formatDuration(detail.data.avgStopSeconds) : null],
    ['Temps sous le seuil', isShelf && detail.data ? formatDuration(detail.data.ruptureMinutes * 60) : null],
  ]

  return (
    <Card title={row.name} aside={
      <Link to={`/cameras?camera=${row.cameraId}`}
        className="inline-flex items-center gap-1.5 rounded-btn border border-line px-3 py-1.5 text-sm font-medium hover:border-brand">
        <Pencil size={15} /> Voir sur la caméra
      </Link>
    }>
      <dl className="mb-4 flex flex-wrap gap-x-8 gap-y-2 text-sm">
        {facts.filter(([, value]) => value !== null).map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs text-muted">{label}</dt>
            <dd className="font-medium">{value}</dd>
          </div>
        ))}
      </dl>
      {!detail.data ? <QueryState error={detail.error} /> : (
        <>
          <ZoneCharts detail={detail.data} timeZone={pharmacy.timeZone} />
          <h3 className="mt-5 mb-1 text-sm font-semibold">
            Alertes de la zone ({formatCount(detail.data.alertCount)})
          </h3>
          <DataTable empty="Aucune alerte sur la période." headers={['Début', 'Alerte', 'Détail', 'État', 'Prise en charge']}
            rows={detail.data.alerts.map((a) => ({
              key: a.id,
              cells: [
                <span className="whitespace-nowrap">
                  {new Date(a.startedAt).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: pharmacy.timeZone })}
                </span>,
                a.title, a.detail, STATUS_LABELS[a.status], a.handledBy ?? '—',
              ],
            }))} />
          {detail.data.alertCount > detail.data.alerts.length && (
            <p className="mt-2 text-xs text-muted">
              Les {detail.data.alerts.length} plus récentes ; le journal complet est dans l'écran Alertes.
            </p>
          )}
        </>
      )}
    </Card>
  )
}
