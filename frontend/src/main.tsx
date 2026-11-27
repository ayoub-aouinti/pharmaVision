import '@fontsource-variable/inter'
import '@fontsource-variable/space-grotesk'
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { ApiError } from './api/client'
import App from './App'
import './index.css'
import { endSession } from './lib/session'

// Session expirée : retour à l'écran de connexion.
const onError = (error: Error) => {
  if (error instanceof ApiError && error.status === 401 && client.getQueryData(['me'])) endSession(client)
}

const client: QueryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: { queries: { staleTime: 5_000, refetchInterval: 30_000, retry: 1 } },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
