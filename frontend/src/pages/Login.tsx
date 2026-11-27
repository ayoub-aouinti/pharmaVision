import { useMutation, useQueryClient } from '@tanstack/react-query'
import { BarChart3 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../api/client'
import { Button } from '../components/ui'
import { startSession } from '../lib/session'

export default function Login() {
  const client = useQueryClient()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const login = useMutation({
    mutationFn: () => api.login(email, password),
    onSuccess: (user) => startSession(client, user),
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    login.mutate()
  }
  const message = login.error instanceof ApiError && login.error.status === 429
    ? 'Trop de tentatives. Réessayez dans une minute.'
    : login.error?.message

  return (
    <main className="grid min-h-screen place-items-center bg-ink p-4">
      <form onSubmit={submit} className="w-full max-w-sm space-y-4 rounded-card bg-white p-6">
        <h1 className="flex items-center gap-2 text-2xl font-bold">
          <BarChart3 className="text-brand" /> PharmaVision
        </h1>
        <label className="block text-sm font-medium">
          Adresse e-mail
          <input className="field mt-1" type="email" required autoComplete="username" autoFocus value={email}
            onChange={(e) => setEmail(e.target.value)} />
        </label>
        <label className="block text-sm font-medium">
          Mot de passe
          <input className="field mt-1" type="password" required autoComplete="current-password" value={password}
            onChange={(e) => setPassword(e.target.value)} />
        </label>
        {message && <p role="alert" className="text-sm text-danger">{message}</p>}
        <Button type="submit" variant="primary" className="w-full" disabled={login.isPending}>
          {login.isPending ? 'Connexion…' : 'Se connecter'}
        </Button>
        <p className="text-xs text-muted">Version de démonstration, données fictives.</p>
      </form>
    </main>
  )
}
