import type { CSSProperties, ReactNode } from 'react'

export interface PlanCell {
  id: number
  x: number | null
  y: number | null
  w: number | null
  h: number | null
  /** 0 à 1 ; ignoré quand une couleur est imposée. */
  intensity: number
  label: string
  content: ReactNode
  color?: { background: string; text: string }
  attention?: boolean
}

// Rampe séquentielle mono-teinte (vert), faible → forte.
const RAMP = ['#e8f4ef', '#c2e3d5', '#8fcdb3', '#4fb28c', '#1c8c63', '#0b5c40']
const step = (intensity: number) => Math.min(RAMP.length - 1, Math.floor(intensity * RAMP.length))

export function RampLegend() {
  return (
    <div className="flex items-center gap-2 text-xs text-muted">
      Peu de passage
      <div className="flex overflow-hidden rounded-full">
        {RAMP.map((c) => <span key={c} className="h-2.5 w-6" style={{ background: c }} />)}
      </div>
      beaucoup
    </div>
  )
}

/** Plan schématique de l'officine (grille de 12 colonnes). */
export default function PlanGrid({ cells, rowHeight = '2.4rem', legend = true, onSelect }: {
  cells: PlanCell[]
  rowHeight?: string
  legend?: boolean
  onSelect?: (id: number) => void
}) {
  return (
    <>
      <div className="grid grid-cols-12 gap-1.5 rounded-2xl bg-bg p-2" style={{ gridAutoRows: rowHeight }}>
        {cells.map((cell) => {
          const level = step(cell.intensity)
          const style: CSSProperties = {
            // Les zones non placées sur le plan se rangent à la suite.
            gridColumn: cell.x === null ? 'span 3' : `${cell.x + 1} / span ${cell.w ?? 2}`,
            gridRow: cell.y === null ? 'span 1' : `${cell.y + 1} / span ${cell.h ?? 2}`,
            background: cell.color?.background ?? RAMP[level],
            color: cell.color?.text ?? (level >= 4 ? '#fff' : '#0f1a17'),
          }
          const props = {
            title: cell.label,
            'aria-label': cell.label,
            style,
            className: `flex min-w-0 flex-col items-center justify-center overflow-hidden rounded-xl p-1 text-center text-xs leading-tight font-semibold break-words transition-[background-color,transform] duration-500 ${cell.attention ? 'needs-attention' : ''}`,
          }
          return onSelect
            ? <button key={cell.id} type="button" {...props} className={`${props.className} hover:scale-[1.02]`}
                onClick={() => onSelect(cell.id)}>{cell.content}</button>
            : <div key={cell.id} tabIndex={0} {...props}>{cell.content}</div>
        })}
      </div>
      {legend && <div className="mt-3"><RampLegend /></div>}
    </>
  )
}
