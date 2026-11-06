// ⚠️ Extension explicite : ce module est chargé par `node --test` via
// secondFacteur.test.ts (CLAUDE.md §10).
import type { EtapeConnexion, ReponseEtape, SaisieSecondFacteur, Session } from '../types/auth.ts'

/**
 * Ce que les écrans du second facteur décident sans le serveur (SPEC.md, étape 23).
 *
 * ⚠️ Les refus locaux ne sont pas du confort, et la raison est propre à ce parcours :
 * **côté serveur, un code mal formé compte comme un échec**, dans les cinq du
 * verrouillage partagé avec le mot de passe. Cinq chiffres tapés au lieu de six, et il
 * reste quatre essais. Refuser ici une saisie qui ne peut pas être un code épargne un
 * essai qui n'en était pas un.
 *
 * Et le tracé du QR code : c'est la partie qui produit une image fausse sans lever
 * d'erreur — un module décalé, et le téléphone ne lit rien.
 */

/** Vrai si `POST /api/auth/connexion` a rendu une étape (202) et non une session. */
export function estEtape(reponse: Session | ReponseEtape): reponse is ReponseEtape {
  return 'etape' in reponse
}

export const LibellesEtape: Readonly<Record<EtapeConnexion, string>> = {
  SecondFacteur: 'Code de vérification',
  Equipement: 'Activer la double authentification',
}

/**
 * Six chiffres, espaces tolérés — les applications affichent souvent « 123 456 ».
 * `null` si la saisie n'a pas cette forme. Miroir de `Totp.LireCode`.
 */
export function lireCode(saisie: string): string | null {
  const chiffres = saisie.replaceAll(' ', '').trim()

  return /^\d{6}$/.test(chiffres) ? chiffres : null
}

/** Alphabet de Crockford, comme `ServiceSecondFacteur` : ni I, ni L, ni O, ni U. */
const AlphabetCodes = '0123456789ABCDEFGHJKMNPQRSTVWXYZ'

/**
 * Un code de récupération ramené à ses seize caractères : majuscules, sans tirets ni
 * espaces, O lu 0, I et L lus 1. `null` s'il n'en a pas la forme. Miroir de
 * `ServiceSecondFacteur.Normaliser` : le serveur refait la même chose, mais refuser ici
 * épargne un essai du verrou.
 */
export function lireCodeRecuperation(saisie: string): string | null {
  const normalise = [...saisie.toUpperCase()]
    .filter((c) => c !== '-' && c !== ' ')
    .map((c) => (c === 'O' ? '0' : c === 'I' || c === 'L' ? '1' : c))
    .join('')

  return normalise.length === 16 && [...normalise].every((c) => AlphabetCodes.includes(c))
    ? normalise
    : null
}

/**
 * Le corps à envoyer, ou le refus à afficher. Une seule des deux clés part : le
 * serveur refuse les deux ensemble, et dirait pourquoi en comptant un échec.
 */
export function saisieDe(
  texte: string,
  recuperation: boolean,
): { corps: SaisieSecondFacteur } | { refus: string } {
  if (recuperation) {
    const code = lireCodeRecuperation(texte)

    return code === null
      ? { refus: 'Un code de récupération compte seize caractères, par exemple ABCD-EFGH-JKMN-PQRS.' }
      : { corps: { codeRecuperation: code } }
  }

  const code = lireCode(texte)

  return code === null
    ? { refus: 'Saisir les six chiffres affichés par l’application.' }
    : { corps: { code } }
}

/** La marge blanche autour du QR code, en modules : la « zone calme » de la norme. */
export const MargeQr = 4

/**
 * Le tracé SVG des modules sombres : un carré d'un module par case, décalé de la
 * marge. Un seul `<path>`, construit en données et jamais injecté en HTML — aucun
 * `innerHTML`, aucune politique de contenu à toucher.
 */
export function traceQr(modules: readonly (readonly boolean[])[]): string {
  const morceaux: string[] = []

  modules.forEach((ligne, y) => {
    ligne.forEach((sombre, x) => {
      if (sombre) morceaux.push(`M${String(x + MargeQr)} ${String(y + MargeQr)}h1v1h-1z`)
    })
  })

  return morceaux.join('')
}

/** Le côté du dessin, marges comprises : la `viewBox` du SVG. */
export function coteQr(modules: readonly (readonly boolean[])[]): number {
  return modules.length + 2 * MargeQr
}

/** Les codes en texte, un par ligne, pour le presse-papiers. */
export function texteDesCodes(codes: readonly string[]): string {
  return ['Codes de récupération KLEAN — chacun ne sert qu’une fois.', '', ...codes].join('\n')
}
