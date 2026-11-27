import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { KeyRound, Pencil, Plus, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { api } from '../../api/client'
import type { CurrentUser, Pharmacy, Role, User } from '../../api/types'
import { DataTable } from '../../components/DataTable'
import { Button, Card, Modal, QueryState } from '../../components/ui'
import { MIN_PASSWORD, PasswordField } from './fields'

const ROLES: Record<Role, string> = { Titulaire: 'Titulaire', Equipe: 'Équipe' }

type Dialog = { kind: 'edit'; user: User | null } | { kind: 'password'; user: User }

export default function UsersPanel({ me, pharmacies }: { me: CurrentUser; pharmacies: Pharmacy[] }) {
  const client = useQueryClient()
  const users = useQuery({ queryKey: ['users'], queryFn: api.users })
  const [dialog, setDialog] = useState<Dialog | null>(null)
  const refresh = () => client.invalidateQueries({ queryKey: ['users'] })
  const remove = useMutation({ mutationFn: api.deleteUser, onSuccess: refresh })
  const names = (ids: number[]) => ids.map((id) => pharmacies.find((p) => p.id === id)?.city ?? `n° ${id}`).join(', ')

  return (
    <Card title="Comptes" aside={
      <Button variant="primary" onClick={() => setDialog({ kind: 'edit', user: null })}>
        <Plus size={15} /> Ajouter un compte
      </Button>
    }>
      <p className="mb-3 text-sm text-muted">
        Le titulaire configure l'officine. L'équipe consulte les écrans et traite les alertes. Tout changement ferme les
        sessions ouvertes du compte concerné.
      </p>
      {remove.error && <p role="alert" className="mb-2 text-sm text-danger">{remove.error.message}</p>}
      {!users.data ? <QueryState error={users.error} /> : (
        <DataTable empty="Aucun compte." headers={['Compte', 'Rôle', 'Officines', 'État', '']}
          rows={users.data.map((user) => ({
            key: user.id,
            cells: [
              <span><span className="block font-medium">{user.displayName}</span><span className="text-xs text-muted">{user.email}</span></span>,
              ROLES[user.role],
              names(user.pharmacyIds),
              !user.isActive ? <span className="text-danger">Désactivé</span>
                : user.locked ? <span className="text-danger">Bloqué (mots de passe erronés)</span> : 'Actif',
              user.id === me.id ? <span className="text-xs text-muted">Votre compte</span> : (
                <div className="flex justify-end gap-1.5">
                  <Button aria-label={`Modifier ${user.email}`} title="Modifier" onClick={() => setDialog({ kind: 'edit', user })}>
                    <Pencil size={15} />
                  </Button>
                  <Button aria-label={`Nouveau mot de passe pour ${user.email}`} title="Nouveau mot de passe"
                    onClick={() => setDialog({ kind: 'password', user })}>
                    <KeyRound size={15} />
                  </Button>
                  <Button variant="danger" aria-label={`Supprimer ${user.email}`} title="Supprimer" disabled={remove.isPending}
                    onClick={() => window.confirm(`Supprimer le compte ${user.email} ?`) && remove.mutate(user.id)}>
                    <Trash2 size={15} />
                  </Button>
                </div>
              ),
            ],
          }))} />
      )}
      {dialog?.kind === 'edit' && (
        <UserDialog user={dialog.user} pharmacies={pharmacies} onClose={() => setDialog(null)} onSaved={refresh} />
      )}
      {dialog?.kind === 'password' && <PasswordDialog user={dialog.user} onClose={() => setDialog(null)} onSaved={refresh} />}
    </Card>
  )
}

function UserDialog({ user, pharmacies, onClose, onSaved }: {
  user: User | null
  pharmacies: Pharmacy[]
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [role, setRole] = useState<Role>(user?.role ?? 'Equipe')
  const [pharmacyIds, setPharmacyIds] = useState<number[]>(user?.pharmacyIds ?? [])
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const save = useMutation({
    mutationFn: () => user
      ? api.updateUser(user.id, { displayName, role, pharmacyIds, isActive })
      : api.createUser({ email, password, displayName, role, pharmacyIds }),
    onSuccess: async () => {
      await onSaved()
      onClose()
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Modal title={user ? `Modifier ${user.email}` : 'Ajouter un compte'} onClose={onClose}>
      <form onSubmit={submit} className="space-y-3 text-sm">
        {!user && (
          <label className="block font-medium">Adresse e-mail
            <input className="field mt-1" type="email" required maxLength={120} autoComplete="off" value={email} autoFocus
              onChange={(e) => setEmail(e.target.value)} />
          </label>
        )}
        <label className="block font-medium">Nom affiché
          <input className="field mt-1" required maxLength={80} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
        </label>
        <label className="block font-medium">Rôle
          <select className="field mt-1" value={role} onChange={(e) => setRole(e.target.value as Role)}>
            {Object.entries(ROLES).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
          </select>
        </label>
        <fieldset>
          <legend className="mb-1 font-medium">Officines accessibles</legend>
          {pharmacies.map((p) => (
            <label key={p.id} className="flex items-center gap-2 py-0.5">
              <input type="checkbox" className="accent-brand-dark" checked={pharmacyIds.includes(p.id)}
                onChange={(e) => setPharmacyIds(e.target.checked ? [...pharmacyIds, p.id] : pharmacyIds.filter((id) => id !== p.id))} />
              {p.name} · {p.city}
            </label>
          ))}
        </fieldset>
        {user ? (
          <label className="flex items-center gap-2 font-medium">
            <input type="checkbox" className="accent-brand-dark" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
            Compte actif
          </label>
        ) : (
          <PasswordField label={`Mot de passe initial (${MIN_PASSWORD} caractères au moins)`} autoComplete="new-password"
            minLength={MIN_PASSWORD} value={password} onChange={setPassword} />
        )}
        {save.error && <p role="alert" className="text-danger">{save.error.message}</p>}
        <div className="flex gap-2">
          <Button type="submit" variant="primary" disabled={save.isPending || pharmacyIds.length === 0}>Enregistrer</Button>
          <Button onClick={onClose}>Annuler</Button>
        </div>
      </form>
    </Modal>
  )
}

function PasswordDialog({ user, onClose, onSaved }: { user: User; onClose: () => void; onSaved: () => Promise<void> }) {
  const [password, setPassword] = useState('')
  const reset = useMutation({
    mutationFn: () => api.resetPassword(user.id, password),
    onSuccess: async () => {
      await onSaved()
      onClose()
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    reset.mutate()
  }

  return (
    <Modal title={`Nouveau mot de passe · ${user.email}`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-3">
        <p className="text-sm text-muted">Le blocage éventuel est levé. Transmettez ce mot de passe à la personne.</p>
        <PasswordField label={`Mot de passe (${MIN_PASSWORD} caractères au moins)`} autoComplete="new-password"
          minLength={MIN_PASSWORD} value={password} onChange={setPassword} />
        {reset.error && <p role="alert" className="text-sm text-danger">{reset.error.message}</p>}
        <div className="flex gap-2">
          <Button type="submit" variant="primary" disabled={reset.isPending}>Enregistrer</Button>
          <Button onClick={onClose}>Annuler</Button>
        </div>
      </form>
    </Modal>
  )
}
