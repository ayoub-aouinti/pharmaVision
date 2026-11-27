import type { QueryClient } from '@tanstack/react-query'
import type { CurrentUser } from '../api/types'

/** Les données d'un compte ne doivent jamais rester en mémoire pour le suivant. */
function forgetData(client: QueryClient) {
  client.removeQueries({ predicate: (query) => query.queryKey[0] !== 'me' })
}

export function startSession(client: QueryClient, user: CurrentUser) {
  forgetData(client)
  client.setQueryData(['me'], user)
}

export function endSession(client: QueryClient) {
  client.setQueryData(['me'], null)
  forgetData(client)
}
