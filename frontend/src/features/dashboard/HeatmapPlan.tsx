import type { UseQueryResult } from '@tanstack/react-query'
import type { HeatCell } from '../../api/types'
import PlanGrid from '../../components/PlanGrid'
import { Card, QueryState } from '../../components/ui'
import { formatDuration } from '../../lib/format'

export default function HeatmapPlan({ query }: { query: UseQueryResult<HeatCell[]> }) {
  return (
    <Card title="Carte de chaleur du plan">
      {!query.data ? <QueryState error={query.error} /> : (
        <PlanGrid cells={query.data.map((cell) => ({
          id: cell.zoneId, x: cell.x, y: cell.y, w: cell.w, h: cell.h, intensity: cell.intensity,
          label: `${cell.name} : ${formatDuration(cell.dwellSeconds)} de présence cumulée`,
          content: cell.name,
        }))} />
      )}
    </Card>
  )
}
