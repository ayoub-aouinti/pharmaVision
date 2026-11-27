import { useMutation, useQueryClient, type UseQueryResult } from '@tanstack/react-query'
import { Image } from 'lucide-react'
import { useState } from 'react'
import { api, thumbnailUrl } from '../../api/client'
import type { Alert, AlertAction, AlertKind } from '../../api/types'
import { Button, Card, Modal, QueryState } from '../../components/ui'
import { formatTime } from '../../lib/format'

const ACTIONS: Record<AlertKind, { action: AlertAction; label: string }> = {
  LowFill: { action: 'restock', label: 'Réassortir' },
  QueueWait: { action: 'notify', label: 'Notifier' },
  Unattended: { action: 'dispatch', label: 'Envoyer' },
  RestrictedPresence: { action: 'notify', label: 'Notifier' },
  CameraOffline: { action: 'diagnose', label: 'Diagnostiquer' },
}

const DONE: Record<AlertAction, string> = {
  restock: 'Réassort demandé',
  notify: 'Équipe notifiée',
  dispatch: 'Conseiller envoyé',
  diagnose: 'Diagnostic en cours',
  resolve: 'Résolue',
}

export default function AlertsPanel({ query, timeZone, className }: {
  query: UseQueryResult<Alert[]>; timeZone: string; className?: string
}) {
  const client = useQueryClient()
  const [viewing, setViewing] = useState<Alert | null>(null)
  const act = useMutation({
    mutationFn: ({ id, action }: { id: number; action: AlertAction }) => api.alertAction(id, action),
    onSettled: () => Promise.all([
      client.invalidateQueries({ queryKey: ['alerts'] }),
      client.invalidateQueries({ queryKey: ['pharmacies'] }),
    ]),
  })

  return (
    <Card title="Alertes & interventions" className={className}
      aside={<span className="text-sm text-muted">{query.data?.length ?? 0} active(s)</span>}>
      {act.error && <p role="alert" className="mb-2 text-sm text-danger">{act.error.message}</p>}
      {!query.data ? <QueryState error={query.error} /> : query.data.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted">Aucune alerte en cours.</p>
      ) : (
        <ul className="max-h-[38rem] space-y-2 overflow-y-auto">
          {query.data.map((alert) => {
            const next = ACTIONS[alert.kind]
            const busy = act.isPending && act.variables.id === alert.id
            return (
              <li key={alert.id}
                className={`rounded-btn border border-line border-l-4 p-3 ${alert.severity === 'High' ? 'border-l-danger' : 'border-l-warn'}`}>
                <p className="text-sm font-semibold">{alert.title}</p>
                <p className="text-sm text-muted">{alert.detail}</p>
                <p className="text-xs text-muted">
                  {alert.severity === 'High' ? 'Priorité forte' : 'Priorité moyenne'} · depuis {formatTime(alert.startedAt, timeZone)}
                </p>
                <div className="mt-2 flex flex-wrap items-center gap-2">
                  {alert.status === 'Open' ? (
                    <Button variant="primary" disabled={busy}
                      onClick={() => act.mutate({ id: alert.id, action: next.action })}>
                      {next.label}
                    </Button>
                  ) : (
                    <>
                      <span className="text-xs font-semibold text-brand-dark">
                        {alert.actionTaken && DONE[alert.actionTaken]}
                      </span>
                      <Button disabled={busy} onClick={() => act.mutate({ id: alert.id, action: 'resolve' })}>
                        Résoudre
                      </Button>
                    </>
                  )}
                  {alert.cameraId && alert.kind !== 'CameraOffline' && (
                    <Button onClick={() => setViewing(alert)}><Image size={15} /> Voir l'image</Button>
                  )}
                </div>
              </li>
            )
          })}
        </ul>
      )}
      {viewing && (
        <Modal title={viewing.title} onClose={() => setViewing(null)}>
          <img src={thumbnailUrl(viewing.id)} alt="Vignette de l'alerte, personnes floutées" className="w-full rounded-btn" />
          <p className="mt-2 text-sm text-muted">Image fictive. Les personnes sont floutées ; aucune vidéo ne quitte la pharmacie.</p>
        </Modal>
      )}
    </Card>
  )
}
