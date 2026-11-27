import { Card } from '../components/ui'

export default function ComingSoon({ title }: { title: string }) {
  return (
    <Card title={title}>
      <p className="text-sm text-muted">Cet écran n'est pas inclus dans la première itération.</p>
    </Card>
  )
}
