import { useState } from 'react'
import { thumbnailUrl } from '../../api/client'
import type { Alert } from '../../api/types'
import { Button } from '../../components/ui'
import { ACTION_DONE, ALERT_KINDS, type useAlertAction } from './shared'

/** Boutons d'intervention selon l'état de l'alerte ; rien une fois résolue. */
export function AlertButtons({ alert, act, onDone }: {
  alert: Alert
  act: ReturnType<typeof useAlertAction>
  onDone?: (alert: Alert) => void
}) {
  if (alert.status === 'Resolved') return null
  const busy = act.isPending && act.variables.id === alert.id
  const next = ALERT_KINDS[alert.kind]
  const run = (action: Parameters<typeof act.mutate>[0]['action']) =>
    act.mutate({ id: alert.id, action }, { onSuccess: onDone })

  return alert.status === 'Open' ? (
    <Button variant="primary" disabled={busy} onClick={() => run(next.action)}>{next.button}</Button>
  ) : (
    <>
      <span className="text-xs font-semibold text-brand-dark">{alert.actionTaken && ACTION_DONE[alert.actionTaken]}</span>
      <Button disabled={busy} onClick={() => run('resolve')}>Résoudre</Button>
    </>
  )
}

export function Thumbnail({ alertId }: { alertId: number }) {
  const [missing, setMissing] = useState<number | null>(null)
  if (missing === alertId) {
    return <p className="rounded-btn bg-bg p-4 text-sm text-muted">Image indisponible : sa durée de conservation est peut-être dépassée.</p>
  }
  return (
    <>
      <img src={thumbnailUrl(alertId)} onError={() => setMissing(alertId)} alt="Vignette de l'alerte, personnes floutées" className="w-full rounded-btn" />
      <p className="mt-2 text-sm text-muted">Les personnes sont floutées ou retirées ; aucune vidéo ne quitte la pharmacie.</p>
    </>
  )
}
