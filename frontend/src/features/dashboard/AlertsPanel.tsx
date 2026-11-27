import type { UseQueryResult } from '@tanstack/react-query'
import { CircleCheckBig, Clock, Image, PackageOpen, ShieldAlert, UserRound, VideoOff } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import type { Alert, AlertKind } from '../../api/types'
import { Button, Card, Modal, QueryState } from '../../components/ui'
import { explain } from '../../lib/glossary'
import { formatTime } from '../../lib/format'
import { AlertButtons, Thumbnail } from '../alerts/AlertActions'
import { useAlertAction } from '../alerts/shared'

const ICONS: Record<AlertKind, typeof Clock> = {
  LowFill: PackageOpen,
  QueueWait: Clock,
  Unattended: UserRound,
  RestrictedPresence: ShieldAlert,
  CameraOffline: VideoOff,
}

/** La liste des choses à faire : les alertes ouvertes, les plus urgentes en tête. */
export default function AlertsPanel({ query, timeZone }: { query: UseQueryResult<Alert[]>; timeZone: string }) {
  const [viewing, setViewing] = useState<Alert | null>(null)
  const act = useAlertAction()

  return (
    <Card title="À faire maintenant" help={explain('alert')} className="flex flex-col"
      aside={<Link to="/alertes" className="text-sm font-semibold text-brand-dark hover:underline">Tout le journal</Link>}>
      {act.error && <p role="alert" className="mb-2 text-sm text-danger">{act.error.message}</p>}
      {!query.data ? <QueryState error={query.error} /> : query.data.length === 0 ? (
        <div className="grid flex-1 place-content-center justify-items-center gap-2 py-10 text-center">
          <CircleCheckBig size={40} className="text-brand" />
          <p className="font-title text-lg font-semibold">Tout est en ordre</p>
          <p className="max-w-60 text-sm text-muted">Une alerte apparaîtra ici dès qu'un rayon ou la file demandera votre attention.</p>
        </div>
      ) : (
        <ul className="max-h-[27rem] divide-y divide-line overflow-y-auto">
          {query.data.map((alert) => {
            const Icon = ICONS[alert.kind]
            const urgent = alert.severity === 'High'
            return (
              <li key={alert.id} className="flex gap-3 py-3.5 first:pt-0 last:pb-0">
                <span className={`grid size-10 shrink-0 place-items-center rounded-full ${urgent ? 'bg-danger text-white' : 'bg-warn/15 text-[#8a4310]'}`}>
                  <Icon size={19} />
                </span>
                <div className="min-w-0">
                  <p className="font-semibold">{alert.title}</p>
                  <p className="text-sm text-muted">{alert.detail}</p>
                  <p className="text-xs text-muted">
                    {urgent && <strong className="text-danger">Urgent. </strong>}
                    Depuis {formatTime(alert.startedAt, timeZone)}
                  </p>
                  <div className="mt-2 flex flex-wrap items-center gap-2">
                    <AlertButtons alert={alert} act={act} />
                    {alert.cameraId && alert.kind !== 'CameraOffline' && (
                      <Button onClick={() => setViewing(alert)}><Image size={15} /> Voir l'image</Button>
                    )}
                  </div>
                </div>
              </li>
            )
          })}
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
