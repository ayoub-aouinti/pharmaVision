import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { Download } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { Alert, AlertFilters, AlertStats, Period, Pharmacy } from '../api/types'
import { Button, Card, Modal, QueryState } from '../components/ui'
import { AlertButtons, Thumbnail } from '../features/alerts/AlertActions'
import AlertCurve from '../features/alerts/AlertCurve'
import { ACTION_DONE, ALERT_KINDS, STATUS_LABELS, useAlertAction } from '../features/alerts/shared'
import { downloadAlerts } from '../lib/exportCsv'
import { formatCount, formatDuration } from '../lib/format'

const EXPORT_LIMIT = 1000

const STATES: { value: AlertFilters['state']; label: string }[] = [
  { value: 'all', label: 'Toutes' },
  { value: 'active', label: 'En cours' },
  { value: 'resolved', label: 'Résolues' },
]

const STATUS_STYLE = {
  Open: 'bg-warn/15 text-[#8a4310]',
  Acknowledged: 'bg-info/15 text-[#1d5a8c]',
  Resolved: 'bg-brand/12 text-brand-dark',
}

const duration = (alert: Alert) =>
  formatDuration(((alert.endedAt ? new Date(alert.endedAt).getTime() : Date.now()) - new Date(alert.startedAt).getTime()) / 1000)

const dateTime = (iso: string, timeZone: string) =>
  new Date(iso).toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone })

export default function Alerts({ pharmacy, period }: { pharmacy: Pharmacy; period: Period }) {
  const [filters, setFilters] = useState<AlertFilters>({ state: 'all', kind: '', severity: '' })
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Alert | null>(null)
  const act = useAlertAction()
  const history = useQuery({
    queryKey: ['alerts', 'history', pharmacy.id, period, filters, page],
    queryFn: () => api.alertHistory(pharmacy.id, period, filters, page),
    placeholderData: keepPreviousData,
  })
  const exportCsv = useMutation({
    mutationFn: () => api.alertHistory(pharmacy.id, period, filters, 1, EXPORT_LIMIT),
    onSuccess: (data) => downloadAlerts(pharmacy, period, data.items),
  })
  const filter = (patch: Partial<AlertFilters>) => {
    setFilters({ ...filters, ...patch })
    setPage(1)
  }

  const data = history.data
  // La fiche ouverte suit les mises à jour en direct.
  const shown = selected && (data?.items.find((a) => a.id === selected.id) ?? selected)
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <div className="space-y-4">
      {data && <Stats stats={data.stats} />}
      <Card title="Journal des alertes" aside={
        <Button variant="primary" disabled={!data?.total || exportCsv.isPending} onClick={() => exportCsv.mutate()}>
          <Download size={16} /> Exporter (CSV)
        </Button>
      }>
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <div role="group" aria-label="État" className="flex rounded-btn border border-line p-0.5">
            {STATES.map((s) => (
              <button key={s.value} type="button" aria-pressed={filters.state === s.value}
                onClick={() => filter({ state: s.value })}
                className={`rounded-md px-3 py-1 text-sm ${filters.state === s.value ? 'bg-ink text-white' : 'text-muted'}`}>
                {s.label}
              </button>
            ))}
          </div>
          <select aria-label="Type d'alerte" className="field !w-auto" value={filters.kind}
            onChange={(e) => filter({ kind: e.target.value as AlertFilters['kind'] })}>
            <option value="">Tous les types</option>
            {Object.entries(ALERT_KINDS).map(([kind, { label }]) => <option key={kind} value={kind}>{label}</option>)}
          </select>
          <select aria-label="Priorité" className="field !w-auto" value={filters.severity}
            onChange={(e) => filter({ severity: e.target.value as AlertFilters['severity'] })}>
            <option value="">Toutes les priorités</option>
            <option value="High">Priorité forte</option>
            <option value="Medium">Priorité moyenne</option>
          </select>
          <p className="text-xs text-muted">La période se choisit en haut ; les alertes en cours restent toujours listées.</p>
        </div>

        {(act.error ?? exportCsv.error) && (
          <p role="alert" className="mb-2 text-sm text-danger">{(act.error ?? exportCsv.error)!.message}</p>
        )}
        {exportCsv.data && exportCsv.data.total > EXPORT_LIMIT && (
          <p className="mb-2 text-sm text-danger">
            Export limité aux {EXPORT_LIMIT} alertes les plus récentes sur {exportCsv.data.total}.
          </p>
        )}

        {!data ? <QueryState error={history.error} /> : data.items.length === 0 ? (
          <p className="py-8 text-center text-sm text-muted">Aucune alerte pour ces critères.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-line text-left text-xs text-muted">
                  {['Début', 'Alerte', 'Priorité', 'État', 'Prise en charge', 'Durée', ''].map((h, i) => (
                    <th key={i} className="py-2 pr-3 font-medium">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="tabular-nums">
                {data.items.map((alert) => (
                  <tr key={alert.id} className="border-b border-line last:border-0">
                    <td className="py-2.5 pr-3 whitespace-nowrap">{dateTime(alert.startedAt, pharmacy.timeZone)}</td>
                    <td className="pr-3">
                      <p className="font-medium">{alert.title}</p>
                      <p className="text-xs text-muted">{alert.detail}</p>
                    </td>
                    <td className={`pr-3 whitespace-nowrap ${alert.severity === 'High' ? 'font-semibold text-danger' : ''}`}>
                      {alert.severity === 'High' ? 'Forte' : 'Moyenne'}
                    </td>
                    <td className="pr-3">
                      <span className={`rounded-full px-2 py-0.5 text-xs font-semibold whitespace-nowrap ${STATUS_STYLE[alert.status]}`}>
                        {STATUS_LABELS[alert.status]}
                      </span>
                    </td>
                    <td className="pr-3">{alert.handledBy ?? '—'}</td>
                    <td className="pr-3 whitespace-nowrap">{duration(alert)}</td>
                    <td className="text-right">
                      <Button onClick={() => { act.reset(); setSelected(alert) }}>Détails</Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {data && data.total > 0 && (
          <nav aria-label="Pagination" className="mt-3 flex items-center justify-end gap-2 text-sm">
            <span className="mr-auto text-muted">{formatCount(data.total)} alerte(s)</span>
            <Button disabled={data.page <= 1} onClick={() => setPage(data.page - 1)}>Précédent</Button>
            <span>Page {data.page} sur {pages}</span>
            <Button disabled={data.page >= pages} onClick={() => setPage(data.page + 1)}>Suivant</Button>
          </nav>
        )}
      </Card>

      {shown && (
        <Modal title={shown.title} onClose={() => setSelected(null)}>
          {shown.cameraId && shown.kind !== 'CameraOffline' && <Thumbnail alertId={shown.id} />}
          <p className="mt-3 text-sm">{shown.detail}</p>
          <Timeline alert={shown} timeZone={pharmacy.timeZone} />
          <AlertCurve alert={shown} timeZone={pharmacy.timeZone} />
          {act.error && <p role="alert" className="mt-2 text-sm text-danger">{act.error.message}</p>}
          <div className="mt-4 flex flex-wrap items-center gap-2">
            <AlertButtons alert={shown} act={act} onDone={setSelected} />
          </div>
        </Modal>
      )}
    </div>
  )
}

function Stats({ stats }: { stats: AlertStats }) {
  const time = (seconds: number | null) => (seconds === null ? '—' : formatDuration(seconds))
  const items: [string, string, ReactNode][] = [
    ['Alertes', formatCount(stats.total), `${formatCount(stats.resolved)} résolue(s)`],
    ['En cours', formatCount(stats.active), 'Ouvertes ou prises en charge'],
    ['Délai moyen de prise en charge', time(stats.avgAcknowledgeSeconds), 'Entre le début et la première action'],
    ['Durée moyenne', time(stats.avgResolutionSeconds), `${formatCount(stats.autoResolved)} close(s) sans intervention`],
  ]
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
      {items.map(([label, value, note]) => (
        <Card key={label}>
          <p className="text-sm text-muted">{label}</p>
          <p className="mt-1 font-title text-3xl font-semibold">{value}</p>
          <p className="mt-1 text-xs text-muted">{note}</p>
        </Card>
      ))}
    </div>
  )
}

function Timeline({ alert, timeZone }: { alert: Alert; timeZone: string }) {
  const steps = [
    { at: alert.startedAt, text: 'Alerte déclenchée' },
    alert.acknowledgedAt && {
      at: alert.acknowledgedAt,
      text: `${alert.actionTaken ? ACTION_DONE[alert.actionTaken] : 'Prise en charge'} par ${alert.handledBy}`,
    },
    alert.endedAt && {
      at: alert.endedAt,
      text: alert.resolvedBy ? `Résolue par ${alert.resolvedBy}` : 'Close automatiquement',
    },
  ].filter((s) => !!s)

  return (
    <ol className="mt-3 space-y-2 border-l-2 border-line pl-4 text-sm">
      {steps.map((step) => (
        <li key={step.text}>
          <span className="font-medium">{step.text}</span>
          <span className="block text-xs text-muted">{dateTime(step.at, timeZone)}</span>
        </li>
      ))}
    </ol>
  )
}
