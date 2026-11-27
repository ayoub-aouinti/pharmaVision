import type { UseQueryResult } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import type { Alert, HeatCell, ShelfRow } from '../../api/types'
import PlanGrid, { RampLegend, type PlanCell } from '../../components/PlanGrid'
import { Card, QueryState, Segmented } from '../../components/ui'
import { explain } from '../../lib/glossary'
import { formatDuration } from '../../lib/format'

type View = 'state' | 'crowd'

const STATE_COLORS = {
  Conforme: { background: '#dff2ea', text: '#0b4f37' },
  StockBas: { background: '#fbdfc7', text: '#6e340b' },
  Rupture: { background: '#c9541a', text: '#ffffff' },
  Inconnu: { background: '#e3e9e7', text: '#53665f' },
}
const NEUTRAL = { background: '#ffffff', text: '#53665f' }

/** Pièce centrale de l'accueil : l'officine vue de dessus, mise à jour en direct. */
export default function FloorPlan({ heatmap, shelves, alerts }: {
  heatmap: UseQueryResult<HeatCell[]>
  shelves: UseQueryResult<ShelfRow[]>
  alerts: Alert[]
}) {
  const [view, setView] = useState<View>('state')
  const navigate = useNavigate()
  const byZone = new Map(shelves.data?.map((s) => [s.zoneId, s]))
  const alerted = new Set(alerts.map((a) => a.zoneId))

  const cells: PlanCell[] = (heatmap.data ?? []).map((cell) => {
    const shelf = byZone.get(cell.zoneId)
    const fill = shelf && shelf.fill !== null ? `${Math.round(shelf.fill)} %` : null
    const attention = alerted.has(cell.zoneId)
    return {
      id: cell.zoneId, x: cell.x, y: cell.y, w: cell.w, h: cell.h, intensity: cell.intensity,
      color: view === 'crowd' ? undefined : shelf ? STATE_COLORS[shelf.state] : NEUTRAL,
      attention: view === 'state' && attention,
      label: [
        cell.name,
        fill && `rempli à ${fill}`,
        `${formatDuration(cell.dwellSeconds)} de présence`,
        attention && 'intervention demandée',
      ].filter(Boolean).join(', '),
      content: (
        <>
          <span>{cell.name}</span>
          {view === 'state' && fill && <span className="mt-0.5 font-title text-xl font-bold tabular-nums">{fill}</span>}
        </>
      ),
    }
  })

  return (
    <Card title="Votre pharmacie en direct" help={view === 'crowd' ? explain('presence') : explain('fill')}
      aside={
        <Segmented label="Affichage du plan" value={view} onChange={setView}
          options={[{ value: 'state', label: 'Rayons' }, { value: 'crowd', label: 'Affluence' }]} />
      }>
      {!heatmap.data ? <QueryState error={heatmap.error} /> : (
        <>
          <PlanGrid cells={cells} rowHeight="3.1rem" legend={false} onSelect={(id) => navigate(`/zones?zone=${id}`)} />
          <div className="mt-3 flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
            {view === 'crowd' ? <RampLegend /> : (
              <ul className="flex flex-wrap gap-x-4 gap-y-1">
                {([['Bien rempli', 'Conforme'], ['À surveiller', 'StockBas'], ['À remplir', 'Rupture']] as const).map(([label, state]) => (
                  <li key={state} className="flex items-center gap-1.5">
                    <span className="size-3 rounded" style={{ background: STATE_COLORS[state].background }} />{label}
                  </li>
                ))}
              </ul>
            )}
            <p>Touchez une zone pour voir son détail.</p>
          </div>
        </>
      )}
    </Card>
  )
}
