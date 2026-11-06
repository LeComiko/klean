/**
 * Les comptes de l'entreprise, miroir de `CompteDto`, `CompteCreateDto` et
 * `CompteCreeDto` dans `backend/DTOs/AuthDto.cs` (SPEC.md, étape 24).
 */

export interface Compte {
  id: string
  email: string
  nom: string
  prenom: string
  /** Un des cinq rôles de `Roles` ; vide si la base en a perdu la trace. */
  role: string
  actif: boolean
  secondFacteurActif: boolean
  /** Vrai tant que la personne ne s'est pas connectée une première fois. */
  motDePasseProvisoire: boolean
  agentId: string | null
  clientId: string | null
  /** Horodatage UTC : un instant, pas un jour à minuit. */
  dateCreation: string
  /** Posée, le compte est pseudonymisé ; le serveur le range en fin de liste. */
  dateAnonymisation: string | null
}

/** Le corps de `POST /api/auth/comptes`. Aucun mot de passe : le serveur le tire. */
export interface CreationCompte {
  email: string
  nom: string
  prenom: string
  role: string
  agentId: string | null
  clientId: string | null
}

/** La réponse d'une création. Le mot de passe provisoire n'existe en clair qu'ici. */
export interface CompteCree {
  compte: Compte
  motDePasseProvisoire: string
}
