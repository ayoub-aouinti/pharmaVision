import type { KonvaEventObject } from 'konva/lib/Node'
import { useEffect, useRef, useState } from 'react'
import { Circle, Image as KonvaImage, Layer, Line, Rect, Stage, Text } from 'react-konva'
import type { Point, Zone, ZoneType } from '../../api/types'
import { ZONE_TYPES } from '../../lib/format'

export type Tool = 'select' | 'rectangle' | 'polygon' | 'line' | 'mask'

const clamp = (v: number) => Math.min(1, Math.max(0, Math.round(v * 10_000) / 10_000))

function useImage(src: string) {
  const [image, setImage] = useState<HTMLImageElement>()
  useEffect(() => {
    setImage(undefined)
    const img = new window.Image()
    img.onload = () => setImage(img)
    img.src = src
    return () => { img.onload = null }
  }, [src])
  return image
}

function useWidth() {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  useEffect(() => {
    if (!ref.current) return
    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width))
    observer.observe(ref.current)
    return () => observer.disconnect()
  }, [])
  return [ref, width] as const
}

/** Image de la caméra avec les zones ; les coordonnées sont normalisées entre 0 et 1. */
export default function ZoneCanvas({ src, zones, selected, tool, onSelect, onCreate, onChange }: {
  src: string
  zones: Zone[]
  /** Zone en cours d'édition (brouillon compris), affichée avec ses sommets. */
  selected: Zone | null
  tool: Tool
  onSelect: (zone: Zone) => void
  onCreate: (type: ZoneType, polygon: Point[]) => void
  onChange: (polygon: Point[]) => void
}) {
  const [ref, width] = useWidth()
  const height = (width * 9) / 16
  const image = useImage(src)
  const [points, setPoints] = useState<Point[]>([])
  const [cursor, setCursor] = useState<Point | null>(null)

  useEffect(() => { setPoints([]) }, [tool, src])
  useEffect(() => {
    const cancel = (e: KeyboardEvent) => e.key === 'Escape' && setPoints([])
    window.addEventListener('keydown', cancel)
    return () => window.removeEventListener('keydown', cancel)
  }, [])

  const position = (e: KonvaEventObject<Event>): Point | null => {
    const p = e.target.getStage()?.getPointerPosition()
    return p ? { x: clamp(p.x / width), y: clamp(p.y / height) } : null
  }
  const flat = (list: Point[]) => list.flatMap((p) => [p.x * width, p.y * height])
  const isRect = tool === 'rectangle' || tool === 'mask'

  const finishPolygon = (list: Point[]) => {
    if (list.length < 3) return
    onCreate('Shelf', list)
    setPoints([])
  }

  const down = (e: KonvaEventObject<Event>) => {
    const p = position(e)
    if (!p || tool === 'select') return
    if (isRect) {
      setPoints([p])
      setCursor(p)
    } else if (tool === 'line') {
      if (points.length === 0) return setPoints([p])
      onCreate('CountingLine', [points[0], p])
      setPoints([])
    } else {
      const first = points[0]
      // Un clic près du premier sommet ferme le polygone.
      if (first && points.length >= 3 && Math.hypot((p.x - first.x) * width, (p.y - first.y) * height) < 12)
        return finishPolygon(points)
      setPoints([...points, p])
    }
  }

  const up = (e: KonvaEventObject<Event>) => {
    if (!isRect || points.length === 0) return
    const a = points[0]
    const b = position(e) ?? cursor ?? a
    setPoints([])
    if (Math.abs(b.x - a.x) < 0.02 || Math.abs(b.y - a.y) < 0.02) return
    const [x1, x2] = [Math.min(a.x, b.x), Math.max(a.x, b.x)]
    const [y1, y2] = [Math.min(a.y, b.y), Math.max(a.y, b.y)]
    onCreate(tool === 'mask' ? 'PrivacyMask' : 'Shelf',
      [{ x: x1, y: y1 }, { x: x2, y: y1 }, { x: x2, y: y2 }, { x: x1, y: y2 }])
  }

  // La zone sélectionnée remplace sa version enregistrée et passe au premier plan.
  const shapes = [...zones.filter((z) => z.id !== selected?.id), ...(selected ? [selected] : [])]

  return (
    <div ref={ref} className="overflow-hidden rounded-btn bg-ink"
      style={{ cursor: tool === 'select' ? 'default' : 'crosshair' }}>
      <Stage width={width} height={height}
        onMouseDown={down} onTouchStart={down} onMouseUp={up} onTouchEnd={up}
        onMouseMove={(e) => setCursor(position(e))} onTouchMove={(e) => setCursor(position(e))}>
        <Layer>
          {image && <KonvaImage image={image} width={width} height={height} listening={false} />}
          {shapes.map((zone) => {
            const color = ZONE_TYPES[zone.type].color
            const active = zone === selected
            const closed = zone.type !== 'CountingLine'
            const top = zone.polygon.reduce((best, p) => (p.y < best.y ? p : best), zone.polygon[0])
            return [
              <Line key={`shape-${zone.id}`} points={flat(zone.polygon)} closed={closed}
                stroke={color} strokeWidth={active ? 3 : 2} dash={closed ? undefined : [10, 6]}
                fill={zone.type === 'PrivacyMask' ? 'rgba(15,26,23,0.92)' : closed ? `${color}${active ? '55' : '30'}` : undefined}
                hitStrokeWidth={14} listening={tool === 'select'}
                onClick={() => onSelect(zone)} onTap={() => onSelect(zone)} />,
              <Text key={`label-${zone.id}`} text={zone.name || 'Nouvelle zone'} listening={false}
                x={top.x * width + 6} y={top.y * height + 6} fontSize={13} fontStyle="bold" fill="#fff"
                shadowColor="#0f1a17" shadowBlur={4} shadowOpacity={1} />,
            ]
          })}
          {selected && tool === 'select' && selected.polygon.map((p, i) => (
            <Circle key={`vertex-${selected.id}-${i}`} x={p.x * width} y={p.y * height} radius={7}
              fill="#fff" stroke={ZONE_TYPES[selected.type].color} strokeWidth={3} draggable
              dragBoundFunc={(pos) => ({
                x: Math.min(width, Math.max(0, pos.x)),
                y: Math.min(height, Math.max(0, pos.y)),
              })}
              onDragMove={(e) => onChange(selected.polygon.map((q, j) =>
                j === i ? { x: clamp(e.target.x() / width), y: clamp(e.target.y() / height) } : q))} />
          ))}
          {isRect && points.length > 0 && cursor && (
            <Rect x={points[0].x * width} y={points[0].y * height}
              width={(cursor.x - points[0].x) * width} height={(cursor.y - points[0].y) * height}
              stroke="#fff" dash={[6, 4]} listening={false} />
          )}
          {!isRect && points.length > 0 && (
            <>
              <Line points={flat(cursor ? [...points, cursor] : points)} stroke="#fff" dash={[6, 4]} listening={false} />
              {points.map((p, i) => <Circle key={i} x={p.x * width} y={p.y * height} radius={4} fill="#fff" listening={false} />)}
            </>
          )}
        </Layer>
      </Stage>
      {tool === 'polygon' && points.length >= 3 && (
        <button type="button" className="w-full bg-brand-dark py-1.5 text-sm text-white" onClick={() => finishPolygon(points)}>
          Terminer le polygone ({points.length} points)
        </button>
      )}
    </div>
  )
}
