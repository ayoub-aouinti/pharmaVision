import { Link } from 'react-router-dom'
import { Card } from '../components/ui'
import { GLOSSARY } from '../lib/glossary'

const STEPS = [
  { title: 'Les caméras observent les rayons', text: 'Où les clients passent, où ils s’arrêtent, quels rayons ils touchent. La vidéo reste sur place et personne n’est reconnu.' },
  { title: 'La caisse dit ce qui se vend', text: 'PharmaVision rapproche les ventes de chaque famille de médicaments des gestes observés devant son rayon.' },
  { title: 'Vous changez le placement', text: 'L’application montre les rayons chauds et froids et propose quoi échanger, déplacer ou mieux présenter.' },
]

const SCREENS = [
  { to: '/placement', name: 'Chaud & froid', answer: 'Quels rayons et médicaments marchent, lesquels dorment, et quoi déplacer ?' },
  { to: '/', name: 'Accueil', answer: 'Que dois-je faire maintenant, et comment se passe la journée ?' },
  { to: '/alertes', name: 'Alertes', answer: 'Qu’est-ce qui s’est passé, et qui s’en est occupé ?' },
  { to: '/historique', name: 'Revoir une journée', answer: 'À quelle heure a-t-on manqué de monde ou de produits ?' },
  { to: '/zones', name: 'Rayons & zones', answer: 'Comment marche chaque rayon, la porte et les comptoirs ?' },
  { to: '/produits', name: 'Produits & marques', answer: 'Qu’est-ce qui se vend, et quelle marque progresse ?' },
  { to: '/rapports', name: 'Rapports', answer: 'Que puis-je imprimer ou envoyer à un laboratoire ?' },
  { to: '/cameras', name: 'Caméras', answer: 'Quelles parties de l’image faut-il surveiller ?' },
  { to: '/parametres', name: 'Réglages', answer: 'Qui a accès, que garde-t-on, comment importer la caisse ?' },
]

export default function Help() {
  return (
    <div className="max-w-5xl space-y-5">
      <h1 className="text-3xl font-bold lg:text-4xl">Comprendre PharmaVision</h1>
      {/* Une suite d'étapes : la numérotation dit dans quel ordre les choses arrivent. */}
      <ol className="grid gap-5 md:grid-cols-3">
        {STEPS.map((step, i) => (
          <li key={step.title} className="rounded-card bg-rail p-5 text-white">
            <span className="grid size-9 place-items-center rounded-full bg-brand font-title text-lg font-bold text-rail">{i + 1}</span>
            <h2 className="mt-3 text-lg font-semibold">{step.title}</h2>
            <p className="mt-1 text-sm leading-relaxed text-white/80">{step.text}</p>
          </li>
        ))}
      </ol>
      <Card title="Les mots utilisés">
        <dl className="divide-y divide-line">
          {Object.values(GLOSSARY).map((entry) => (
            <div key={entry.word} className="grid gap-1 py-3 md:grid-cols-[13rem_1fr]">
              <dt className="font-title font-semibold">{entry.word}</dt>
              <dd>
                <p>{entry.meaning}</p>
                <p className="mt-0.5 text-sm text-brand-dark">À quoi ça sert : {entry.use}</p>
              </dd>
            </div>
          ))}
        </dl>
      </Card>
      <Card title="Quel écran ouvrir ?">
        <ul className="grid gap-3 md:grid-cols-2">
          {SCREENS.map((screen) => (
            <li key={screen.to}>
              <Link to={screen.to} className="block h-full rounded-2xl bg-bg p-4 transition-colors hover:bg-brand-soft">
                <span className="block font-title font-semibold">{screen.name}</span>
                <span className="text-sm text-muted">{screen.answer}</span>
              </Link>
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
