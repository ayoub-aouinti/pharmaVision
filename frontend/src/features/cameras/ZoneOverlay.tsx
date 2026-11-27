import type { Zone } from '../../api/types'
import { ZONE_TYPES } from '../../lib/format'

/** Zones dessinées par-dessus l'image d'une caméra (coordonnées normalisées). */
export default function ZoneOverlay({ zones }: { zones: Zone[] }) {
  return (
    <svg viewBox="0 0 1 1" preserveAspectRatio="none" className="absolute inset-0 size-full">
      {zones.map((zone) => {
        const points = zone.polygon.map((p) => `${p.x},${p.y}`).join(' ')
        const color = ZONE_TYPES[zone.type].color
        const shape = {
          points, stroke: color, strokeWidth: 2, vectorEffect: 'non-scaling-stroke' as const,
        }
        return (
          <g key={zone.id}>
            <title>{zone.name}</title>
            {zone.type === 'CountingLine'
              ? <polyline {...shape} fill="none" strokeDasharray="6 4" />
              : <polygon {...shape} fill={color} fillOpacity={zone.type === 'PrivacyMask' ? 1 : 0.18} />}
          </g>
        )
      })}
    </svg>
  )
}
