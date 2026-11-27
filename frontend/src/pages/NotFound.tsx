import { Link } from 'react-router-dom'
import { Card } from '../components/ui'

export default function NotFound() {
  return (
    <Card title="Page introuvable">
      <Link to="/" className="text-sm text-brand-dark underline">Retour au tableau de bord</Link>
    </Card>
  )
}
