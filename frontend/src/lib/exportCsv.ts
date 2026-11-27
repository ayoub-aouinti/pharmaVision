import type { HourPoint, KpiSummary, Period, Pharmacy, ShelfRow } from '../api/types'

// Séparateur « ; » et virgule décimale : ouverture directe dans Excel en français.
function cell(value: string | number | null) {
  if (value === null) return ''
  if (typeof value === 'number') return String(Math.round(value * 1000) / 1000).replace('.', ',')
  // Un texte saisi commençant par = + - @ serait interprété comme une formule.
  const safe = /^[=+\-@]/.test(value) ? `'${value}` : value
  return `"${safe.replaceAll('"', '""')}"`
}

export function downloadReport(
  pharmacy: Pharmacy, period: Period, kpis: KpiSummary, shelves: ShelfRow[], hourly: HourPoint[],
) {
  const rows: (string | number | null)[][] = [
    ['PharmaVision (données fictives)', pharmacy.name, pharmacy.city],
    ['Période', period, kpis.from, kpis.to],
    [],
    ['Indicateur', 'Valeur', 'Unité', 'Tendance'],
    ...kpis.items.map((k) => [k.label, k.value, k.unit, k.trend]),
    [],
    ['Linéaire', 'Remplissage %', 'État', 'Arrêts', 'Engagement', 'Prise en main', 'Transformation', 'Rupture (min)'],
    ...shelves.map((s) => [s.name, s.fill, s.state, s.stops, s.engagementRate, s.pickupRate, s.transformation, s.ruptureMinutes]),
    [],
    ['Heure', 'Visiteurs', 'Tickets'],
    ...hourly.map((h) => [h.hour, h.visitors, h.tickets]),
  ]
  const csv = '﻿' + rows.map((r) => r.map(cell).join(';')).join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `pharmavision-${pharmacy.city.toLowerCase()}-${period}-${kpis.to.slice(0, 10)}.csv`
  link.click()
  URL.revokeObjectURL(url)
}
