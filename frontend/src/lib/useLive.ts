import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import type { Period } from '../api/types'

/**
 * Écoute le hub temps réel et rafraîchit les requêtes concernées.
 * Les mesures arrivent en continu : le rafraîchissement est limité, davantage sur les longues périodes.
 */
export function useLive(pharmacyId: number | undefined, period: Period) {
  const client = useQueryClient()
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    if (!pharmacyId) return
    const throttle = period === 'live' || period === 'today' ? 4_000 : 30_000
    let timer: number | undefined
    let disposed = false
    let retry: number | undefined

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/live')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 5_000 })
      .configureLogging(LogLevel.None)
      .build()

    const refreshMeasures = () => {
      timer ??= window.setTimeout(() => {
        timer = undefined
        for (const key of ['kpis', 'shelves', 'heatmap', 'hourly', 'cameras', 'pharmacies'])
          void client.invalidateQueries({ queryKey: [key] })
      }, throttle)
    }
    const refreshAlerts = () => {
      void client.invalidateQueries({ queryKey: ['alerts'] })
      void client.invalidateQueries({ queryKey: ['pharmacies'] })
    }

    connection.on('kpiUpdate', refreshMeasures)
    connection.on('zoneUpdate', refreshMeasures)
    connection.on('alertRaised', refreshAlerts)
    connection.on('alertsChanged', refreshAlerts)

    const join = async () => {
      await connection.invoke('Join', pharmacyId)
      setConnected(true)
    }
    connection.onreconnecting(() => setConnected(false))
    connection.onreconnected(() => void join())

    const start = async () => {
      if (disposed || connection.state !== HubConnectionState.Disconnected) return
      try {
        await connection.start()
        await join()
      } catch {
        if (!disposed) retry = window.setTimeout(start, 5_000)
      }
    }
    connection.onclose(() => setConnected(false))
    const started = start()

    return () => {
      disposed = true
      window.clearTimeout(timer)
      window.clearTimeout(retry)
      setConnected(false)
      void started.finally(() => connection.stop())
    }
  }, [client, pharmacyId, period])

  return connected
}
