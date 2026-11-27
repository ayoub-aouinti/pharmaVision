import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { api } from '../api/client'
import type { Period, Pharmacy } from '../api/types'
import { Button } from '../components/ui'
import AlertsPanel from '../features/dashboard/AlertsPanel'
import CameraFeeds from '../features/dashboard/CameraFeeds'
import HeatmapPlan from '../features/dashboard/HeatmapPlan'
import HourlyChart from '../features/dashboard/HourlyChart'
import KpiCards from '../features/dashboard/KpiCards'
import ShelfTable from '../features/dashboard/ShelfTable'
import { downloadReport } from '../lib/exportCsv'

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
    <div key={id} className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted">
          {kpis.data ? `Occupation estimée : ${kpis.data.occupancy} personne(s)` : ' '}
        </p>
        <Button variant="primary" disabled={!exportable}
          onClick={() => exportable && downloadReport(pharmacy, period, kpis.data, shelves.data, hourly.data)}>
          <Download size={16} /> Exporter (CSV)
        </Button>
      </div>
      <KpiCards query={kpis} period={period} />
      <div className="grid gap-4 xl:grid-cols-3">
        <CameraFeeds query={cameras} className="xl:col-span-2" />
        <HeatmapPlan query={heatmap} />
        <ShelfTable query={shelves} className="xl:col-span-2" />
        <AlertsPanel query={alerts} timeZone={pharmacy.timeZone} className="xl:row-span-2" />
        <HourlyChart query={hourly} period={period} className="xl:col-span-2" />
      </div>
    </div>
  )
}
