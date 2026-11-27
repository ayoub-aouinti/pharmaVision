import type { Kpi, ZoneType } from '../api/types'

const integer = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 0 })
const percent = new Intl.NumberFormat('fr-FR', { style: 'percent', maximumFractionDigits: 0 })

const money = new Intl.NumberFormat('fr-FR', {
  style: 'currency', currency: 'TND', minimumFractionDigits: 0, maximumFractionDigits: 0,
})

export const formatMoney = (v: number) => money.format(v)
export const formatCount = (v: number) => integer.format(v)
export const formatPercent = (v: number) => percent.format(v)

export function formatDuration(seconds: number) {
  const s = Math.round(seconds)
  if (s < 60) return `${s} s`
  if (s < 3600) return `${Math.floor(s / 60)} min ${String(s % 60).padStart(2, '0')}`
  return `${Math.floor(s / 3600)} h ${String(Math.floor((s % 3600) / 60)).padStart(2, '0')}`
}

export const formatKpi = (kpi: Kpi) =>
  kpi.unit === 'percent' ? formatPercent(kpi.value)
    : kpi.unit === 'seconds' ? formatDuration(kpi.value)
      : formatCount(kpi.value)

export const formatTime = (iso: string, timeZone?: string) =>
  new Date(iso).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit', timeZone })

export const ZONE_TYPES: Record<ZoneType, { label: string; color: string }> = {
  Shelf: { label: 'Linéaire', color: '#12a06e' },
  Passage: { label: 'Passage', color: '#3d8fd1' },
  Queue: { label: "File d'attente", color: '#e8792b' },
  CountingLine: { label: 'Ligne de comptage', color: '#8ec5e8' },
  Restricted: { label: 'Zone réglementée', color: '#c9541a' },
  PrivacyMask: { label: 'Masque de confidentialité', color: '#0f1a17' },
}
