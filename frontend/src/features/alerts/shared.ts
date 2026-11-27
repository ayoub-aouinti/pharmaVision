import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AlertAction, AlertKind, AlertStatus } from '../../api/types'

export const ALERT_KINDS: Record<AlertKind, { label: string; action: AlertAction; button: string }> = {
  LowFill: { label: 'Rupture de linéaire', action: 'restock', button: 'Réassortir' },
  QueueWait: { label: 'Attente au comptoir', action: 'notify', button: 'Notifier' },
  Unattended: { label: 'Client sans conseil', action: 'dispatch', button: 'Envoyer' },
  RestrictedPresence: { label: 'Zone réglementée', action: 'notify', button: 'Notifier' },
  CameraOffline: { label: 'Caméra hors ligne', action: 'diagnose', button: 'Diagnostiquer' },
}

export const ACTION_DONE: Record<AlertAction, string> = {
  restock: 'Réassort demandé',
  notify: 'Équipe notifiée',
  dispatch: 'Conseiller envoyé',
  diagnose: 'Diagnostic en cours',
  resolve: 'Résolue',
}

export const STATUS_LABELS: Record<AlertStatus, string> = {
  Open: 'Ouverte',
  Acknowledged: 'Prise en charge',
  Resolved: 'Résolue',
}

export function useAlertAction() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, action }: { id: number; action: AlertAction }) => api.alertAction(id, action),
    onSettled: () => Promise.all([
      client.invalidateQueries({ queryKey: ['alerts'] }),
      client.invalidateQueries({ queryKey: ['pharmacies'] }),
    ]),
  })
}
