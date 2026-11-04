import { useCallback, useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

import {
  creerRessource,
  executerAction,
  listerRessource,
  listerComptes,
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
import type { LienEnfant } from '../composants/TableauRessource'
import { ongletsDe } from '../layout/sections'
import type { Section } from '../layout/sections'
import { descriptionDe, enfantsDe } from '../reference/descriptions'
import {
  accorder,
  corpsDe,
  departCreation,
  departOuverture,
  designer,
  filtreDe,
  majuscule,
  nouveau,
  rendu,
  texteDe,
  tousLes,
} from '../reference/valeurs'
import { aUnRole } from '../types/auth'
import type { ColonneRessource, DescriptionRessource } from '../types/reference'
import './Reference.css'

interface Proprietes {
  description: DescriptionRessource
  /** La section qui porte la ressource, pour la sous-navigation. */
  section: Section
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
 * Un seul motif, piloté par `reference/descriptions.ts`. Sept écrans écrits à la
 * main auraient sept fois l'occasion de diverger, et la divergence prend une forme
 * précise : un bouton d'enregistrement qui répond 403.
 *
 * Cette enveloppe ne fait qu'une chose : lire le filtre de **descente de niveau**
 * dans la chaîne de requête, et **remonter l'écran** quand il change.
 *
 * ⚠️ La remontée par `key` n'est pas un raffinement. La pagination, le formulaire
 * ouvert et la confirmation de suppression en cours appartiennent à une liste ; les
 * traîner d'une liste filtrée à une autre afficherait « page 3 » sur une liste qui
 * n'en a qu'une, ou une confirmation portant sur une ligne devenue invisible. C'est
 * la même raison qui fait qu'`App.tsx` remonte déjà cet écran d'une ressource à
 * l'autre.
 */
export function Reference({ description, section }: Proprietes) {
  const [parametres] = useSearchParams()
  const filtre = filtreDe(description, (nom) => parametres.get(nom))

  return (
    <EcranReference
      key={filtre?.valeur ?? 'tout'}
      description={description}
      section={section}
      filtreChamp={filtre?.champ}
      filtreValeur={filtre?.valeur}
    />
  )
}

interface ProprietesEcran extends Proprietes {
  /**
   * Le filtre est passé en **deux primitives**, et non en objet.
   *
   * ⚠️ `filtreDe` rend un objet neuf à chaque rendu : placé dans les dépendances de
   * l'effet de chargement, il le relancerait indéfiniment. Deux chaînes se comparent
   * par valeur, donc l'effet déclare honnêtement ce dont il dépend.
   */
  filtreChamp?: string
  filtreValeur?: string
}

function EcranReference({
  description,
  section,
  filtreChamp,
  filtreValeur,
}: ProprietesEcran) {
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

  const [actionEnCours, setActionEnCours] = useState(false)

  const [annonce, setAnnonce] = useState<string | null>(null)
  const [alerte, setAlerte] = useState<string | null>(null)

  const [choix, setChoix] = useState<Record<string, Choix>>({})

  const cle = `${description.cle}#${filtreValeur ?? ''}#${String(page)}#${String(rechargement)}`
  const enErreur = echouee?.cle === cle
  const chargement = obtenue?.cle !== cle && !enErreur

  // ---- La liste ----
  useEffect(() => {
    const controleur = new AbortController()

    const filtre = filtreChamp !== undefined && filtreValeur !== undefined
      ? { champ: filtreChamp, valeur: filtreValeur }
      : undefined

    listerRessource<Ressource>(
      description.route,
      page,
      TailleParDefaut,
      controleur.signal,
      filtre,
    )
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
  }, [cle, description.route, description.libelle, page, filtreChamp, filtreValeur])

  // ---- Les listes déroulantes des champs de référence ----
  //
  // Chargées une fois par ressource affichée, et non à l'ouverture du formulaire :
  // les noms des clients servent aussi à rendre la colonne « Client » du tableau, et
  // à nommer le parent dans le bandeau de descente de niveau.
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

      listerToutPourChoix<Ressource>(cible.routeChoix ?? cible.route, controleur.signal)
        .then((recue) => {
          setChoix((precedent) => ({
            ...precedent,
            [parente]: {
              options: recue.elements.map((element) => ({
                valeur: element.id,
                libelle: texteDe(element[cible.etiquette]) ?? element.id,
                // Une ressource sans `actif` reste toujours proposée.
                actif: element.actif !== false,
                ligne: element,
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

  // ---- Les listes déroulantes des champs de compte ----
  //
  // Seulement pour qui écrit : les routes de comptes lui sont réservées, et un lecteur
  // n'ouvre le formulaire qu'en lecture seule, où la valeur n'a pas à être choisie.
  // Rangées dans `choix` sous leur route, qu'aucune clé de description ne peut
  // prendre puisqu'elle commence par une barre.
  useEffect(() => {
    if (!ecriture) return

    const controleur = new AbortController()

    const routes = [
      ...new Set(
        description.champs
          .map((champ) => champ.comptes)
          .filter((route): route is string => route !== undefined),
      ),
    ]

    for (const route of routes) {
      listerComptes(route, controleur.signal)
        .then((comptes) => {
          setChoix((precedent) => ({
            ...precedent,
            [route]: {
              // Un compte désactivé reste proposé et le dit : des agents peuvent lui
              // être encore rattachés, et l'option manquante viderait le champ à
              // l'affichage sans rien dire.
              options: comptes.map((compte) => ({
                valeur: compte.id,
                libelle: `${compte.prenom} ${compte.nom}${compte.actif ? '' : ' (désactivé)'}`,
              })),
              tronquee: false,
            },
          }))
        })
        .catch(() => {
          // Silencieux, pour la même raison que les champs de référence.
        })
    }

    return () => {
      controleur.abort()
    }
  }, [description, ecriture])
  const lectureSeule = !ecriture
  const designation = designer(description.singulier, description.genre)

  const fermer = useCallback(() => {
    setEdition(null)
    setErreurFormulaire(null)
  }, [])

  /**
   * Résout l'identifiant d'une ressource parente en son libellé.
   *
   * ⚠️ Un identifiant absent d'une liste **complète** désigne une ligne que le serveur
   * ne montre pas à cet utilisateur : le titulaire d'une prestation dans l'équipe d'un
   * autre chef (SPEC.md, étape 19). L'afficher en UUID ne dirait rien. Sur une liste
   * tronquée, en revanche, l'absence ne prouve rien, et l'identifiant reste.
   */
  function libelleParent(parente: string, identifiant: string): string {
    const liste = choix[parente]
    const option = liste?.options.find((candidate) => candidate.valeur === identifiant)

    if (option !== undefined) return option.libelle

    return liste !== undefined && !liste.tronquee ? 'Hors de votre périmètre' : identifiant
  }

  function celluleDe(ligne: Ressource, colonne: ColonneRessource): string {
    return rendu(ligne, colonne, description.champs, libelleParent)
  }

  function etiquetteDe(ligne: Ressource): string {
    return texteDe(ligne[description.etiquette]) ?? description.singulier
  }

  /**
   * Les liens de descente de niveau d'une ligne.
   *
   * ⚠️ Filtrés par les rôles de lecture de l'**enfant**, et non par ceux de cet
   * écran. Un `ClientSyndic` lit les sites d'un client mais pas leurs contrats : lui
   * proposer « Voir les contrats » depuis la liste des sites serait un lien vers un
   * 403, c'est-à-dire la faute d'interface que tout ce motif existe pour éviter.
   */
  function liensEnfants(ligne: Ressource): readonly LienEnfant[] {
    if (profil === null) return []

    return enfantsDe(description.cle)
      .filter((enfant) => aUnRole(profil, enfant.rolesLecture))
      .map((enfant) => ({
        cle: enfant.cle,
        libelle: `Voir les ${enfant.libelle.toLowerCase()}`,
        // `parent.champ` existe forcément : c'est lui qui a fait trouver cet enfant.
        vers: `${enfant.chemin}?${enfant.parent?.champ ?? ''}=${encodeURIComponent(ligne.id)}`,
      }))
  }

  async function enregistrer(valeurs: ValeursFormulaire) {
    if (edition === null) return

    setEnregistrement(true)
    setErreurFormulaire(null)

    const corps = corpsDe(description.champs, valeurs)

    try {
      if (edition.mode === 'creation') {
        await creerRessource<Ressource>(description.route, corps)
        setAnnonce(`${majuscule(description.singulier)} créé${accorder(description.genre)}.`)
      } else {
        await modifierRessource(description.route, edition.ligne.id, corps)
        // La ressource est nommée, pas seulement la ligne : « Lundi, mercredi,
        // vendredi enregistré » ne dit pas de quoi il s'agit.
        setAnnonce(
          `${majuscule(designation)} « ${etiquetteDe(edition.ligne)} » `
          + `enregistré${accorder(description.genre)}.`,
        )
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

  /**
   * ⚠️ Le compte rendu passe par l'annonce, comme une création : c'est la seule zone
   * lue par un lecteur d'écran sans y déplacer le focus. Un refus passe par l'alerte,
   * avec le message du serveur.
   */
  async function agir() {
    const action = description.action

    if (action === undefined) return

    setActionEnCours(true)
    setAnnonce(null)
    setAlerte(null)

    try {
      setAnnonce(action.compteRendu(await executerAction(action.route)))
      setRechargement((tour) => tour + 1)
    } catch (echec: unknown) {
      setAlerte(messageDe(echec, `${action.libelle} : échec.`))
    } finally {
      setActionEnCours(false)
    }
  }

  async function supprimer(ligne: Ressource) {
    setSuppressionEnCours(ligne.id)
    setAlerte(null)

    const nom = etiquetteDe(ligne)

    try {
      await supprimerRessource(description.route, ligne.id)

      setAnnonce(`${majuscule(designation)} « ${nom} » supprimé${accorder(description.genre)}.`)
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

  const onglets = ongletsDe(section, profil?.roles ?? [])

  const optionsParChamp: Record<string, readonly OptionReference[]> = {}
  const tronqueesParChamp: Record<string, boolean> = {}

  for (const champ of description.champs) {
    const source = champ.reference ?? champ.comptes

    if (source === undefined) continue

    optionsParChamp[champ.nom] = choix[source]?.options ?? []
    tronqueesParChamp[champ.nom] = choix[source]?.tronquee === true
  }

  // ---- La descente de niveau ----
  const parent = description.parent
  const filtre = filtreChamp !== undefined && filtreValeur !== undefined
  const descriptionParente = parent === undefined ? undefined : descriptionDe(parent.ressource)

  /**
   * Le parent déjà choisi dans le formulaire de création.
   *
   * ⚠️ Ce n'est pas du confort : sur l'écran des prestations, la liste déroulante des
   * contrats les désigne par leur fréquence, et deux contrats peuvent la partager.
   * Venir du contrat lui-même est le seul chemin où ce choix ne se pose pas.
   */
  const imposees: ValeursFormulaire
    = filtreChamp !== undefined && filtreValeur !== undefined
      ? { [filtreChamp]: filtreValeur }
      : {}

  return (
    <>
      <EnTetePage
        titre={description.libelle}
        sousTitre={description.sousTitre}
        actions={
          ecriture ? (
            <>
              {description.action !== undefined && (
                <button
                  className="reference__action"
                  type="button"
                  disabled={actionEnCours}
                  onClick={() => void agir()}
                >
                  {actionEnCours ? description.action.enCours : description.action.libelle}
                </button>
              )}
              <button
                className="reference__nouveau"
                type="button"
                onClick={() => {
                  setEdition({ mode: 'creation' })
                  setErreurFormulaire(null)
                  setAnnonce(null)
                }}
              >
                {nouveau(description.singulier, description.genre)}
              </button>
            </>
          ) : undefined
        }
      />

      <SousNavigation onglets={onglets} />

      {filtre && parent !== undefined && (
        <p className="reference__filtre">
          <span className="reference__filtre-texte">
            {description.libelle} de « {libelleParent(parent.ressource, filtreValeur)} »
          </span>
          {/* Le retour à la liste complète est indispensable : sans lui, la seule
              sortie d'une liste filtrée est la sous-navigation, qui ramène sur la
              même ressource et garde donc la chaîne de requête. */}
          <Link className="reference__filtre-tout" to={description.chemin}>
            Voir {tousLes(description.genre)} {description.libelle.toLowerCase()}
          </Link>
        </p>
      )}

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
              designation={designation}
              liensEnfants={liensEnfants}
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
              videMessage={messageVide(description, ecriture, filtre, descriptionParente)}
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
                  ? nouveau(description.singulier, description.genre)
                  : `${lectureSeule ? '' : 'Modifier '}${etiquetteDe(edition.ligne)}`
              }
              champs={description.champs}
              depart={
                edition.mode === 'creation'
                  ? departCreation(description.champs, imposees)
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

/**
 * Le message d'une liste vide.
 *
 * Il change sous filtre, et ce n'est pas cosmétique : « Aucun site pour l'instant »
 * au-dessus d'un bandeau « Sites de Bellevue » laisse croire que l'entreprise n'a
 * aucun site, alors qu'elle n'en a aucun **sur ce client**.
 */
function messageVide(
  description: DescriptionRessource,
  ecriture: boolean,
  filtre: boolean,
  parente: DescriptionRessource | undefined,
): string {
  const quoi = filtre
    ? `Aucun${accorder(description.genre)} ${description.singulier} sur ${
      parente === undefined ? 'ce parent' : designer(parente.singulier, parente.genre)
    }`
    : `Aucun${accorder(description.genre)} ${description.singulier} pour l’instant`

  if (!ecriture) return `${quoi}.`

  const ouvre = `Le bouton « ${nouveau(description.singulier, description.genre)} » ouvre le formulaire`

  if (filtre && parente !== undefined) {
    return `${quoi}. ${ouvre}, avec ${designer(parente.singulier, parente.genre)} déjà choisi.`
  }

  return description.action === undefined
    ? `${quoi}. ${ouvre}.`
    : `${quoi}. ${ouvre}. ${description.action.suggestion}`
}
