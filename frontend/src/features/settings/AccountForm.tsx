import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import type { CurrentUser } from '../../api/types'
import { Button, Card } from '../../components/ui'
import { MIN_PASSWORD, PasswordField } from './fields'

export default function AccountForm({ user }: { user: CurrentUser }) {
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const change = useMutation({
    mutationFn: () => api.changePassword(current, next),
    onSuccess: () => {
      setCurrent('')
      setNext('')
      setConfirm('')
    },
  })
  const mismatch = confirm !== '' && next !== confirm
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (next === confirm) change.mutate()
  }

  return (
    <Card title="Mon mot de passe">
      <p className="mb-3 text-sm text-muted">{user.name} · {user.email}</p>
      <form onSubmit={submit} className="max-w-sm space-y-3">
        <PasswordField label="Mot de passe actuel" autoComplete="current-password" value={current} onChange={setCurrent} />
        <PasswordField label={`Nouveau mot de passe (${MIN_PASSWORD} caractères au moins)`} autoComplete="new-password"
          minLength={MIN_PASSWORD} value={next} onChange={setNext} />
        <PasswordField label="Confirmer le nouveau mot de passe" autoComplete="new-password" value={confirm} onChange={setConfirm} />
        {mismatch && <p role="alert" className="text-sm text-danger">Les deux saisies diffèrent.</p>}
        {change.error && <p role="alert" className="text-sm text-danger">{change.error.message}</p>}
        {change.isSuccess && (
          <p role="status" className="text-sm text-brand-dark">Mot de passe modifié. Les autres sessions sont fermées.</p>
        )}
        <Button type="submit" variant="primary" disabled={change.isPending || mismatch}>Changer le mot de passe</Button>
      </form>
    </Card>
  )
}
