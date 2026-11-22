/**
 * La logique de l'assistant d'installation (SPEC.md, étape 34, lot A2), hors du composant :
 * c'est la seule partie de l'écran qui décide de **ne pas** appeler le serveur, et celle qui
 * écrit le corps envoyé. Enfermée dans un `.tsx`, elle ne s'éprouverait pas (CLAUDE.md §11).
 *
 * ⚠️ Le serveur reste l'autorité sur tout : ces refus épargnent un essai, surtout celui du code,
 * que le plafond de débit compte. Mot de passe : seulement la présence et la confirmation,
 * Identity juge le reste et répond en français (`auth/motDePasse.ts`).
 *
 * ⚠️ Le code d'installation ne passe jamais dans une URL.
 */

// ⚠️ Extensions `.ts` explicites : `node --test` ne résout pas sans (CLAUDE.md §10).
import type { CorpsInstallation } from '../api/installation'
import { refusPremierMotDePasse } from '../auth/motDePasse.ts'
import { Territoires } from '../reference/descriptions.ts'

export type Etape = 'Code' | 'Entreprise' | 'Compte' | 'Savoir'

export interface Saisie {
  code: string
  raisonSociale: string
  siret: string
  /** Un nom de `Territoires` ; `Metropole` au départ. */
  territoire: string
  email: string
  emailConfirmation: string
  nom: string
  prenom: string
  motDePasse: string
  confirmation: string
}

export const SaisieVide: Readonly<Saisie> = Object.freeze({
  code: '',
  raisonSociale: '',
  siret: '',
  territoire: 'Metropole',
  email: '',
  emailConfirmation: '',
  nom: '',
  prenom: '',
  motDePasse: '',
  confirmation: '',
})

/** Celui de `CodeInstallation.Alphabet` : sans I, L, O, 0 ni 1. */
export const AlphabetCode = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'

/** Le même texte que `InstallationController.DebutRefusCode`. */
export const DebutRefusCode = "Code d'installation incorrect"

/** Le même texte que `InstallationController.DejaInstallee` : le seul 409 qui mène à la connexion. */
export const MessageDejaInstallee = 'KLEAN est déjà installé : connectez-vous.'

const LongueurCode = 20

/**
 * `char.IsWhiteSpace` de .NET, et non le `\s` de JavaScript : ils divergent — `\s` retient
 * U+FEFF, que .NET ignore, et ignore U+0085, que .NET retient. Un code doit se lire pareil
 * des deux côtés.
 */
const Blanc = /[\t-\r \u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]/

/** Ce que `CodeInstallation.Normaliser` retire en plus des blancs. */
export const Tirets: ReadonlySet<string> = new Set(['-', '\u00AD', '\u200B', '\u2010', '\u2011', '\u2012', '\u2013', '\u2014', '\u2212', '\uFF0D'])

/**
 * `char.ToUpperInvariant` : un caractère pour un caractère, jamais « ß » → « SS ». Un point hors
 * du plan de base reste inchangé, comme en .NET. Les seules divergences mesurées avec .NET sur
 * tous les points de code sont « ı » et les lettres grecques à iota souscrit : toutes hors de
 * l'alphabet, donc sans effet sur un code juste.
 */
function majuscule(c: string): string {
  const m = c.toUpperCase()
  return m.length === 1 ? m : c
}

/** Retire blancs et tirets — ceux d'un copier-coller compris — et met en majuscules. */
export function normaliserCode(saisie: string): string {
  let sortie = ''
  for (const c of saisie) {
    // `for…of` itère par points de code, le serveur par unités UTF-16 : aucun des caractères
    // retirés n'est hors du plan de base, et un point hors plan reste hors de l'alphabet.
    if (Blanc.test(c) || Tirets.has(c)) continue
    sortie += majuscule(c)
  }
  return sortie
}

export function refusCode(saisie: string): string | null {
  const code = normaliserCode(saisie)

  for (const c of code) {
    if (AlphabetCode.includes(c)) continue
    if ('ILO01'.includes(c)) return "Relisez le code : les lettres I, L, O et les chiffres 0, 1 n'y figurent pas."
    // Un caractère invisible (U+FEFF, U+200C, U+2060) se nomme : l'afficher ne dirait rien.
    const invisible = /[\p{Cc}\p{Cf}\p{Z}]/u.test(c)
    const nom = invisible ? `U+${(c.codePointAt(0) ?? 0).toString(16).toUpperCase().padStart(4, '0')}` : `« ${c} »`
    return `Le caractère ${nom} n'existe pas dans un code d'installation.`
  }

  if (code.length !== LongueurCode) {
    return `Le code compte ${String(LongueurCode)} caractères ; la saisie en compte ${String(code.length)}.`
  }

  return null
}

/** `String.Trim` de .NET : les mêmes blancs que `normaliserCode`. */
function rogner(texte: string): string {
  let debut = 0
  let fin = texte.length
  while (debut < fin && Blanc.test(texte[debut])) debut++
  while (fin > debut && Blanc.test(texte[fin - 1])) fin--
  return texte.slice(debut, fin)
}

/** `char.IsControl` : U+0000 à U+001F, U+007F à U+009F. PostgreSQL refuse NUL dans un texte. */
function estControle(c: string): boolean {
  const n = c.charCodeAt(0)
  return n <= 0x1f || (n >= 0x7f && n <= 0x9f)
}

const ContientControle = (texte: string): boolean => [...texte].some(estControle)

const MessageControle = 'La raison sociale, le nom et le prénom ne peuvent pas contenir de caractère de contrôle.'

/** Le SIRET sans ses espaces : le serveur ne retire que cela. */
function siretSansEspaces(siret: string): string {
  return [...siret].filter((c) => !Blanc.test(c)).join('')
}

export function refusEntreprise(
  s: Pick<Saisie, 'raisonSociale' | 'siret' | 'territoire'>,
): string | null {
  const raison = rogner(s.raisonSociale)

  if (ContientControle(raison)) return MessageControle
  if (raison.length === 0 || raison.length > 200) return 'La raison sociale est obligatoire, 200 caractères au plus.'
  if (!/^[0-9]{14}$/.test(siretSansEspaces(s.siret))) return 'Le SIRET compte 14 chiffres.'
  if (!(Territoires as readonly string[]).includes(s.territoire)) return 'Choisissez un territoire.'

  return null
}

/**
 * Règle minimale : un `@` entre deux parties non vides, sans blanc. Le serveur juge. L'écran est
 * un peu plus strict que lui sur les blancs — il refuse toute adresse qui en contient, là où le
 * serveur ne rogne que les extrémités —, et c'est délibéré. La confirmation ne distingue pas
 * les majuscules : la casse d'une adresse ne change pas la faute de frappe qu'elle attrape.
 */
function adresseVraisemblable(adresse: string): boolean {
  const arobase = adresse.indexOf('@')
  return (
    arobase > 0 &&
    arobase < adresse.length - 1 &&
    adresse.length <= 256 &&
    ![...adresse].some((c) => Blanc.test(c))
  )
}

export function refusCompte(
  s: Pick<Saisie, 'email' | 'emailConfirmation' | 'nom' | 'prenom' | 'motDePasse' | 'confirmation'>,
): string | null {
  const email = rogner(s.email)
  const nom = rogner(s.nom)
  const prenom = rogner(s.prenom)

  if (!adresseVraisemblable(email)) return "L'adresse électronique n'est pas valide."
  if (email.toLowerCase() !== rogner(s.emailConfirmation).toLowerCase()) return 'Les deux saisies de l’adresse diffèrent.'
  if (ContientControle(nom) || ContientControle(prenom)) return MessageControle
  if (nom.length === 0 || nom.length > 100) return 'Le nom est obligatoire, 100 caractères au plus.'
  if (prenom.length === 0 || prenom.length > 100) return 'Le prénom est obligatoire, 100 caractères au plus.'

  return refusPremierMotDePasse(s.motDePasse, s.confirmation)
}

/**
 * Le corps de `POST /api/installation`. Ni `confirmation` ni `emailConfirmation` : elles
 * n'existent que dans l'écran. Le mot de passe part tel quel, sans rognage.
 */
export function corpsInstallation(s: Saisie): CorpsInstallation {
  return {
    code: normaliserCode(s.code),
    raisonSociale: rogner(s.raisonSociale),
    siret: siretSansEspaces(s.siret),
    territoire: s.territoire,
    email: rogner(s.email),
    nom: rogner(s.nom),
    prenom: rogner(s.prenom),
    motDePasse: s.motDePasse,
  }
}

/**
 * Où mène un refus du serveur :
 *
 * - `Code` : 400 ou 429 dont le message commence par `DebutRefusCode` — le code est faux, ou le
 *   plafond global des codes faux est plein, ce que le 429 dit en commençant pareil ;
 * - `Connexion` : le 409 « déjà installé » — un compte existe, l'assistant n'a plus d'objet ;
 * - `Bloquee` : tout autre 409. Le serveur n'en a qu'un, l'entreprise orpheline, qui couvre aussi
 *   le SIRET pris : sans aucun compte, une entreprise au même SIRET est forcément orpheline.
 *   ⚠️ Aucun retour à l'entreprise : changer de SIRET contournerait le blocage et laisserait
 *   l'orpheline en base. Elle se retire à la main, puis KLEAN redémarre ;
 * - `SurPlace` : le reste — un 400 de champ ou d'Identity, un 429 de plafond par adresse, une
 *   panne : le message, sur l'écran en cours.
 */
export type SuiteDuRefus = 'Code' | 'Connexion' | 'Bloquee' | 'SurPlace'

export function refusDInstallation(
  statut: number,
  message: string,
): { suite: SuiteDuRefus; message: string } {
  if ((statut === 400 || statut === 429) && message.startsWith(DebutRefusCode)) return { suite: 'Code', message }
  if (statut === 409) return { suite: message === MessageDejaInstallee ? 'Connexion' : 'Bloquee', message }
  return { suite: 'SurPlace', message }
}
