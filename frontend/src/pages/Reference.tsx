import { useCallback, useEffect, useState } from 'react'

import {
  creerRessource,
  listerRessource,
  listerToutPourChoix,
  modifierRessource,
  supprimerRessource,
  TailleParDefaut,
} from '../api/ressources'
import type { Page, Ressource } from '../api/ressources'
import { useAuth } from '../auth/contexte'
import { FormulaireRessource } from '../composants/FormulaireRessource'
import type { OptionReference } from '../composants/FormulaireRessource'
import type { ValeursFormulaire } from '../reference/valeurs'
import { EnTetePage } from '../composants/EnTetePage'
import { Pagination } from '../composants/Pagination'
import { SousNavigation } from '../composants/SousNavigation'
import { TableauRessource } from '../composants/TableauRessource'
import { descriptionDe, descriptionsLisibles } from '../reference/descriptions'
import {
  corpsDe,
  departCreation,
  departOuverture,
  rendu,
  texteDe,
} from '../reference/valeurs'
import { aUnRole } from '../types/auth'
import type { ColonneRessource, DescriptionRessource } from '../types/reference'
import './Reference.css'

interface Proprietes {
  description: DescriptionRessource
  /** Les ressources de la section, pour la sous-navigation. */
  fratrie: readonly string[]
}

/** Résultat obtenu, avec la clé de la demande à laquelle il répond. */
interface Obtenue {
  cle: string
  page: Page<Ressource>
}

interface Echouee {
  cle: string
  message: string
}

/** Options d'une ressource parente, pour une liste déroulante. */
interface Choix {
  options: OptionReference[]
  /** Le serveur en a plus qu'il n'en a rendu : le choix est incomplet. */
  tronquee: boolean
}

type Edition =
  | { mode: 'creation' }
  | { mode: 'ouverture'; ligne: Ressource }

function messageDe(echec: unknown, repli: string): string {
  return echec instanceof Error ? echec.message : repli
}

/**
 * L'écran d'une ressource de référence, **quelle qu'elle soit**.
 *
 * Un seul motif, piloté par `reference/descriptions.ts`. Six écrans écrits à la
 * main auraient six fois l'occasion de diverger, et la divergence prend une forme
 * précise : un bouton d'enregistrement qui répond 403.
 *
 * Comme `Planning.tsx`, aucun état `chargement` n'est stocké : chaque résultat
 * porte la clé de la demande à laquelle il répond, et l'on charge tant que ni le
 * résultat ni l'échec ne portent la clé courante. Un booléen posé à la main dans un
 * effet déclenche un rendu de plus et peut se désynchroniser — c'est ce que signale
 * `react(set-state-in-effect)`, qui avait raison à l'étape 14.
 */
export function Reference({ description, fratrie }: Proprietes) {
  const { profil } = useAuth()

  const [page, setPage] = useState(1)
  const [rechargement, setRechargement] = useState(0)
  const [obtenue, setObtenue] = useState<Obtenue | null>(null)
  const [echouee, setEchouee] = useState<Echouee | null>(null)

  const [edition, setEdition] = useState<Edition | null>(null)
  const [enregistrement, setEnregistrement] = useState(false)
  const [erreurFormulaire, setErreurFormulaire] = useState<string | null>(null)

  const [aConfirmer, setAConfirmer] = useState<string | null>(null)
  const [suppressionEnCours, setSuppressionEnCours] = useState<string | null>(null)

  const [annonce, setAnnonce] = useState<string | null>(null)
  const [alerte, setAlerte] = useState<string | null>(null)

  const [choix, setChoix] = useState<Record<string, Choix>>({})

  const cle = `${description.cle}#${String(page)}#${String(rechargement)}`
  const enErreur = echouee?.cle === cle
  const chargement = obtenue?.cle !== cle && !enErreur

  // ---- La liste ----
  useEffect(() => {
    const controleur = new AbortController()

    listerRessource<Ressource>(description.route, page, TailleParDefaut, controleur.signal)
      .then((recue) => {
        setObtenue({ cle, page: recue })
      })
      .catch((echec: unknown) => {
        // Une requête abandonnée n'est pas un échec : c'est le clic suivant.
        if (controleur.signal.aborted) return

        setEchouee({ cle, message: messageDe(echec, `${description.libelle} indisponibles.`) })
      })

    return () => {
      controleur.abort()
    }
  }, [cle, description.route, description.libelle, page])

  // ---- Les listes déroulantes des champs de référence ----
  //
  // Chargées une fois par ressource affichée, et non à l'ouverture du formulaire :
  // les noms des clients servent aussi à rendre la colonne « Client » du tableau.
  useEffect(() => {
    const controleur = new AbortController()

    const parentes = [
      ...new Set(
        description.champs
          .map((champ) => champ.reference)
          .filter((reference): reference is string => reference !== undefined),
      ),
    ]

    for (const parente of parentes) {
      const cible = descriptionDe(parente)

      if (cible === undefined) continue

      listerToutPourChoix<Ressource>(cible.route, controleur.signal)
        .then((recue) => {
          setChoix((precedent) => ({
            ...precedent,
            [parente]: {
              options: recue.elements.map((element) => ({
                valeur: element.id,
                libelle: texteDe(element[cible.etiquette]) ?? element.id,
              })),
              tronquee: recue.total > recue.elements.length,
            },
          }))
        })
        .catch(() => {
          // Silencieux à dessein : une liste déroulante vide se voit, et une
          // deuxième bannière d'erreur au-dessus de la première n'aide personne.
          // Le champ restera « — choisir — » sans option, et l'enregistrement
          // échouera avec le message du serveur.
        })
    }

    return () => {
      controleur.abort()
    }
  }, [description])

  const ecriture = profil !== null && aUnRole(profil, description.rolesEcriture)
  const lectureSeule = !ecriture

  const fermer = useCallback(() => {
    setEdition(null)
    setErreurFormulaire(null)
  }, [])

  /** Résout l'identifiant d'une ressource parente en son libellé. */
  function libelleParent(parente: string, identifiant: string): string {
    const option = choix[parente]?.options.find((candidate) => candidate.valeur === identifiant)

    return option?.libelle ?? identifiant
  }

  function celluleDe(ligne: Ressource, colonne: ColonneRessource): string {
    return rendu(ligne, colonne, description.champs, libelleParent)
  }

  function etiquetteDe(ligne: Ressource): string {
    return texteDe(ligne[description.etiquette]) ?? description.singulier
  }

  async function enregistrer(valeurs: ValeursFormulaire) {
    if (edition === null) return

    setEnregistrement(true)
    setErreurFormulaire(null)

    const corps = corpsDe(description.champs, valeurs)

    try {
      if (edition.mode === 'creation') {
        await creerRessource<Ressource>(description.route, corps)
        setAnnonce(`${majuscule(description.singulier)} créé${accord(description)}.`)
      } else {
        await modifierRessource(description.route, edition.ligne.id, corps)
        setAnnonce(`« ${etiquetteDe(edition.ligne)} » enregistré${accord(description)}.`)
      }

      setAlerte(null)
      setEdition(null)
      setRechargement((tour) => tour + 1)
    } catch (echec: unknown) {
      setErreurFormulaire(messageDe(echec, 'Enregistrement refusé.'))
    } finally {
      setEnregistrement(false)
    }
  }

  async function supprimer(ligne: Ressource) {
    setSuppressionEnCours(ligne.id)
    setAlerte(null)

    const nom = etiquetteDe(ligne)

    try {
      await supprimerRessource(description.route, ligne.id)

      setAnnonce(`« ${nom} » supprimé${accord(description)}.`)
      setAConfirmer(null)

      // Le formulaire pouvait montrer la ligne qui vient de disparaître.
      if (edition?.mode === 'ouverture' && edition.ligne.id === ligne.id) setEdition(null)

      setRechargement((tour) => tour + 1)
    } catch (echec: unknown) {
      // ⚠️ Le message du serveur, pas un message inventé. Une suppression refusée
      // l'est presque toujours en 409 par une clé étrangère — « des données
      // rattachées l'empêchent » — et c'est exactement ce que l'utilisateur doit
      // lire pour savoir quoi supprimer d'abord.
      setAlerte(messageDe(echec, 'Suppression refusée.'))
      setAConfirmer(null)
    } finally {
      setSuppressionEnCours(null)
    }
  }

  const ressourcesDeLaSection = descriptionsLisibles(fratrie, profil?.roles ?? [])

  const optionsParChamp: Record<string, readonly OptionReference[]> = {}
  const tronqueesParChamp: Record<string, boolean> = {}

  for (const champ of description.champs) {
    if (champ.reference === undefined) continue

    optionsParChamp[champ.nom] = choix[champ.reference]?.options ?? []
    tronqueesParChamp[champ.nom] = choix[champ.reference]?.tronquee === true
  }

  return (
    <>
      <EnTetePage
        titre={description.libelle}
        sousTitre={description.sousTitre}
        actions={
          ecriture ? (
            <button
              className="reference__nouveau"
              type="button"
              onClick={() => {
                setEdition({ mode: 'creation' })
                setErreurFormulaire(null)
                setAnnonce(null)
              }}
            >
              Nouveau {description.singulier}
            </button>
          ) : undefined
        }
      />

      <SousNavigation ressources={ressourcesDeLaSection} />

      {annonce !== null && <output className="reference__annonce">{annonce}</output>}

      {alerte !== null && (
        <p className="reference__alerte" role="alert">
          <strong className="reference__alerte-mot">Refusé —</strong> {alerte}
        </p>
      )}

      {enErreur && (
        <p className="reference__erreur" role="alert">
          <strong className="reference__alerte-mot">Erreur —</strong> {echouee.message}
        </p>
      )}

      {/* En erreur, la liste disparaît : la garder afficherait les lignes d'une
          autre page sous une pagination qui annonce celle-ci. */}
      {!enErreur && obtenue === null && chargement && (
        <output className="reference__attente">Chargement…</output>
      )}

      {!enErreur && obtenue !== null && (
        <div className="reference" aria-busy={chargement}>
          <div className="reference__principal">
            <TableauRessource
              colonnes={description.colonnes}
              lignes={obtenue.page.elements}
              choisie={edition?.mode === 'ouverture' ? edition.ligne.id : null}
              rendu={celluleDe}
              etiquette={etiquetteDe}
              libelleModifier={lectureSeule ? 'Voir' : 'Modifier'}
              onModifier={(ligne) => {
                setEdition({ mode: 'ouverture', ligne })
                setErreurFormulaire(null)
                setAnnonce(null)
              }}
              onSupprimer={ecriture ? (ligne) => void supprimer(ligne) : undefined}
              aConfirmer={aConfirmer}
              onDemanderSuppression={ecriture ? setAConfirmer : undefined}
              enCours={suppressionEnCours}
              videMessage={
                ecriture
                  ? `Aucun ${description.singulier} pour l’instant. Le bouton « Nouveau ${description.singulier} » ouvre le formulaire.`
                  : `Aucun ${description.singulier} à afficher.`
              }
            />

            <Pagination
              page={obtenue.page.page}
              taille={obtenue.page.taille}
              total={obtenue.page.total}
              chargement={chargement}
              onChanger={(suivante) => {
                setPage(suivante)
                setAConfirmer(null)
              }}
            />
          </div>

          {edition !== null && (
            <FormulaireRessource
              // ⚠️ La `key` remonte le formulaire quand la ligne change. C'est ce
              // qui permet à `depart` d'être lu une seule fois : synchroniser un
              // état interne sur une propriété par un effet est le défaut que
              // `react(set-state-in-effect)` signale.
              key={edition.mode === 'creation' ? 'creation' : edition.ligne.id}
              titre={
                edition.mode === 'creation'
                  ? `Nouveau ${description.singulier}`
                  : `${lectureSeule ? '' : 'Modifier '}${etiquetteDe(edition.ligne)}`
              }
              champs={description.champs}
              depart={
                edition.mode === 'creation'
                  ? departCreation(description.champs)
                  : departOuverture(description.champs, edition.ligne)
              }
              options={optionsParChamp}
              optionsTronquees={tronqueesParChamp}
              lectureSeule={lectureSeule}
              enCours={enregistrement}
              erreur={erreurFormulaire}
              onEnregistrer={(valeurs) => void enregistrer(valeurs)}
              onAnnuler={fermer}
            />
          )}
        </div>
      )}
    </>
  )
}

function majuscule(mot: string): string {
  return mot.charAt(0).toUpperCase() + mot.slice(1)
}

/** Accord du participe passé : « créé » ou « créée ». */
function accord(description: DescriptionRessource): string {
  return description.genre === 'feminin' ? 'e' : ''
}
