import type { UseQueryResult } from '@tanstack/react-query'
import type { HeatCell } from '../../api/types'
import { Card, QueryState } from '../../components/ui'
import { formatDuration } from '../../lib/format'

// Rampe séquentielle mono-teinte (vert), faible → forte.
const RAMP = ['#e8f4ef', '#c2e3d5', '#8fcdb3', '#4fb28c', '#1c8c63', '#0b5c40']
const step = (intensity: number) => Math.min(RAMP.length - 1, Math.floor(intensity * RAMP.length))

export default function HeatmapPlan({ query }: { query: UseQueryResult<HeatCell[]> }) {
  const cells = query.data
  return (
    <Card title="Carte de chaleur du plan">
      {!cells ? <QueryState error={query.error} /> : (
        <>
          <div className="grid grid-cols-12 gap-0.5 rounded-btn border border-line bg-bg p-1"
            style={{ gridAutoRows: '2.4rem' }}>
            {cells.map((cell) => {
              const level = step(cell.intensity)
              const label = `${cell.name} : ${formatDuration(cell.dwellSeconds)} de présence cumulée`
              return (
                <div key={cell.zoneId} title={label} aria-label={label} tabIndex={0}
                  className="flex min-w-0 items-center justify-center overflow-hidden rounded-md p-1 text-center text-[11px] leading-tight font-medium break-words"
                  style={{
                    // Les zones non placées sur le plan se rangent à la suite.
                    gridColumn: cell.x === null ? 'span 3' : `${cell.x + 1} / span ${cell.w ?? 2}`,
                    gridRow: cell.y === null ? 'span 1' : `${cell.y + 1} / span ${cell.h ?? 2}`,
                    background: RAMP[level],
                    color: level >= 4 ? '#fff' : '#0f1a17',
                  }}>
                  {cell.name}
                </div>
              )
            })}
          </div>
          <div className="mt-3 flex items-center gap-2 text-xs text-muted">
            Présence faible
            <div className="flex">
              {RAMP.map((c) => <span key={c} className="h-3 w-6" style={{ background: c }} />)}
            </div>
            forte
          </div>
        </>
      )}
    </Card>
  )
}
