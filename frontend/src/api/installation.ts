import { api } from './client'

export interface EtatInstallation {
  installee: boolean
}

export interface CorpsInstallation {
  code: string
  raisonSociale: string
  siret: string
  territoire: string
  email: string
  nom: string
  prenom: string
  motDePasse: string
}

// ⚠️ Le code part dans le corps, jamais dans l'URL : une URL se journalise, se range dans
// l'historique et se transmet. Ces routes ne rendent jamais 401 (session) : rien à ajouter
// à `SansRafraichissement`.
export const lireEtatInstallation = (signal?: AbortSignal) =>
  api.get<EtatInstallation>('/api/installation', { signal })

export const installer = (corps: CorpsInstallation, signal?: AbortSignal) =>
  api.post<void>('/api/installation', corps, signal)
