import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { api } from '../api/client'
import type { Period, Pharmacy } from '../api/types'
import { Button } from '../components/ui'
import Activity from '../features/dashboard/Activity'
import AlertsPanel from '../features/dashboard/AlertsPanel'
import Brief from '../features/dashboard/Brief'
import CameraFeeds from '../features/dashboard/CameraFeeds'
import FloorPlan from '../features/dashboard/FloorPlan'
import ShelfTable from '../features/dashboard/ShelfTable'
import { downloadReport } from '../lib/exportCsv'

/** Phrase d'accueil : ce qui se passe et ce qui attend l'équipe. */
function headline(alerts: number | undefined) {
  if (alerts === undefined) return 'Bonjour'
  if (alerts === 0) return 'Tout va bien dans la pharmacie'
  return alerts === 1 ? '1 chose demande votre attention' : `${alerts} choses demandent votre attention`
}

export default function Dashboard({ pharmacy, period }: { pharmacy: Pharmacy; period: Period }) {
  const id = pharmacy.id
  // Pendant un changement de période, les anciens chiffres restent affichés ; jamais ceux d'une autre pharmacie.
  const options = { placeholderData: keepPreviousData }
  const kpis = useQuery({ queryKey: ['kpis', id, period], queryFn: () => api.kpis(id, period), ...options })
  const shelves = useQuery({ queryKey: ['shelves', id, period], queryFn: () => api.shelves(id, period), ...options })
  const heatmap = useQuery({ queryKey: ['heatmap', id, period], queryFn: () => api.heatmap(id, period), ...options })
  const hourly = useQuery({ queryKey: ['hourly', id, period], queryFn: () => api.hourly(id, period), ...options })
  const cameras = useQuery({ queryKey: ['cameras', id], queryFn: () => api.cameras(id), refetchInterval: 10_000 })
  const alerts = useQuery({ queryKey: ['alerts', id], queryFn: () => api.alerts(id), refetchInterval: 10_000 })

  const exportable = kpis.data && shelves.data && hourly.data

  return (
    <div key={id} className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-3xl font-bold lg:text-4xl">{headline(alerts.data?.length)}</h1>
          <p className="mt-1 text-muted">
            {kpis.data
              ? `Environ ${kpis.data.occupancy} personne(s) dans la pharmacie en ce moment.`
              : 'Chargement des mesures…'}
          </p>
        </div>
        <Button disabled={!exportable}
          onClick={() => exportable && downloadReport(pharmacy, period, kpis.data, shelves.data, hourly.data)}>
          <Download size={16} /> Exporter (CSV)
        </Button>
      </div>
      <div className="grid gap-5 xl:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
        <AlertsPanel query={alerts} timeZone={pharmacy.timeZone} />
        <FloorPlan heatmap={heatmap} shelves={shelves} alerts={alerts.data ?? []} />
      </div>
      <Activity kpis={kpis} hourly={hourly} period={period} />
      <div className="grid gap-5 xl:grid-cols-3">
        <ShelfTable query={shelves} className="xl:col-span-2" />
        <Brief shelves={shelves.data ?? []} hourly={hourly.data ?? []} />
      </div>
      <CameraFeeds query={cameras} />
    </div>
  )
}
