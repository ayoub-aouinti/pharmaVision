/** Les mots de l'application, expliqués simplement. Utilisés par les bulles d'aide et la page Aide. */
export const GLOSSARY = {
  visitors: {
    word: 'Visiteur',
    meaning: 'Une personne qui franchit la porte d’entrée.',
    use: 'Prévoir le personnel aux heures chargées.',
  },
  conversion: {
    word: 'Visiteurs qui achètent',
    meaning: 'Sur 100 personnes entrées, combien repartent avec un ticket de caisse.',
    use: 'Voir si les visites deviennent des ventes.',
  },
  wait: {
    word: 'Attente au comptoir',
    meaning: 'Temps moyen passé dans la file avant d’être servi.',
    use: 'Ouvrir un deuxième poste quand l’attente monte.',
  },
  ruptures: {
    word: 'Rayons à remplir',
    meaning: 'Rayons dont le remplissage est passé sous le seuil choisi (30 % au départ).',
    use: 'Réassortir avant de perdre des ventes.',
  },
  stop: {
    word: 'Arrêt',
    meaning: 'Une personne reste plus de 5 secondes devant un rayon.',
    use: 'Savoir quels rayons attirent les clients.',
  },
  engagement: {
    word: 'Intérêt pour les rayons',
    meaning: 'Sur 100 personnes qui passent devant un rayon, combien s’arrêtent.',
    use: 'Juger l’emplacement et la présentation d’un rayon.',
  },
  pickup: {
    word: 'Prise en main',
    meaning: 'Un client touche ou prend un produit. Aujourd’hui seulement simulé : les caméras réelles ne le mesurent pas encore.',
    use: 'Comparer l’intérêt pour les produits.',
  },
  fill: {
    word: 'Remplissage',
    meaning: 'Part du rayon encore garnie de produits. Aujourd’hui seulement simulé.',
    use: 'Réassortir à temps.',
  },
  transformation: {
    word: 'Vente après prise en main',
    meaning: 'Unités vendues d’une famille comparées au nombre de fois où ses produits ont été pris en main.',
    use: 'Repérer un rayon qui intéresse mais vend peu.',
  },
  presence: {
    word: 'Affluence sur le plan',
    meaning: 'Temps total passé par les visiteurs dans chaque zone. Plus la zone est foncée, plus on y reste.',
    use: 'Déplacer une tête de gondole, repérer les zones mortes.',
  },
  alert: {
    word: 'Alerte',
    meaning: 'Un message quand une règle est dépassée : rayon vide, file trop longue, client sans conseil, zone réservée, caméra coupée.',
    use: 'Agir tout de suite, puis garder la trace.',
  },
} as const

export type Term = keyof typeof GLOSSARY

export const explain = (term: Term) => GLOSSARY[term].meaning
