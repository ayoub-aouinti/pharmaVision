import type { ReactNode } from 'react'

export interface PlanCell {
  id: number
  x: number | null
  y: number | null
  w: number | null
  h: number | null
  /** 0 à 1. */
  intensity: number
  label: string
  content: ReactNode
}

// Rampe séquentielle mono-teinte (vert), faible → forte.
const RAMP = ['#e8f4ef', '#c2e3d5', '#8fcdb3', '#4fb28c', '#1c8c63', '#0b5c40']
const step = (intensity: number) => Math.min(RAMP.length - 1, Math.floor(intensity * RAMP.length))

/** Plan schématique de l'officine (grille de 12 colonnes) coloré selon la présence. */
export default function PlanGrid({ cells, rowHeight = '2.4rem' }: { cells: PlanCell[]; rowHeight?: string }) {
  return (
    <>
      <div className="grid grid-cols-12 gap-0.5 rounded-btn border border-line bg-bg p-1" style={{ gridAutoRows: rowHeight }}>
        {cells.map((cell) => {
          const level = step(cell.intensity)
          return (
            <div key={cell.id} title={cell.label} aria-label={cell.label} tabIndex={0}
              className="flex min-w-0 flex-col items-center justify-center overflow-hidden rounded-md p-1 text-center text-[11px] leading-tight font-medium break-words"
              style={{
                // Les zones non placées sur le plan se rangent à la suite.
                gridColumn: cell.x === null ? 'span 3' : `${cell.x + 1} / span ${cell.w ?? 2}`,
                gridRow: cell.y === null ? 'span 1' : `${cell.y + 1} / span ${cell.h ?? 2}`,
                background: RAMP[level],
                color: level >= 4 ? '#fff' : '#0f1a17',
              }}>
              {cell.content}
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
  )
}
