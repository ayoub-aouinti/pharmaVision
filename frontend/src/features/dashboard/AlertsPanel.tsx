import type { UseQueryResult } from '@tanstack/react-query'
import { Image } from 'lucide-react'
import { useState } from 'react'
import type { Alert } from '../../api/types'
import { Button, Card, Modal, QueryState } from '../../components/ui'
import { formatTime } from '../../lib/format'
import { AlertButtons, Thumbnail } from '../alerts/AlertActions'
import { useAlertAction } from '../alerts/shared'

export default function AlertsPanel({ query, timeZone, className }: {
  query: UseQueryResult<Alert[]>; timeZone: string; className?: string
}) {
  const [viewing, setViewing] = useState<Alert | null>(null)
  const act = useAlertAction()

  return (
    <Card title="Alertes & interventions" className={className}
      aside={<span className="text-sm text-muted">{query.data?.length ?? 0} active(s)</span>}>
      {act.error && <p role="alert" className="mb-2 text-sm text-danger">{act.error.message}</p>}
      {!query.data ? <QueryState error={query.error} /> : query.data.length === 0 ? (
        <p className="py-6 text-center text-sm text-muted">Aucune alerte en cours.</p>
      ) : (
        <ul className="max-h-[38rem] space-y-2 overflow-y-auto">
          {query.data.map((alert) => (
            <li key={alert.id}
              className={`rounded-btn border border-line border-l-4 p-3 ${alert.severity === 'High' ? 'border-l-danger' : 'border-l-warn'}`}>
              <p className="text-sm font-semibold">{alert.title}</p>
              <p className="text-sm text-muted">{alert.detail}</p>
              <p className="text-xs text-muted">
                {alert.severity === 'High' ? 'Priorité forte' : 'Priorité moyenne'} · depuis {formatTime(alert.startedAt, timeZone)}
              </p>
              <div className="mt-2 flex flex-wrap items-center gap-2">
                <AlertButtons alert={alert} act={act} />
                {alert.cameraId && alert.kind !== 'CameraOffline' && (
                  <Button onClick={() => setViewing(alert)}><Image size={15} /> Voir l'image</Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
      {viewing && (
        <Modal title={viewing.title} onClose={() => setViewing(null)}>
          <Thumbnail alertId={viewing.id} />
        </Modal>
      )}
    </Card>
  )
}
