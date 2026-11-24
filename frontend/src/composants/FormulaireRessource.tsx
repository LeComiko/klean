import { Fragment, useEffect, useRef, useState } from 'react'

import { masque, optionsProposees, preremplir } from '../reference/valeurs'
import type { Ligne, ValeursFormulaire } from '../reference/valeurs'
import {
  AideLocaliser,
  MotAVerifier,
  bornesDe,
  chaineCoordonnee,
  corpsLocalisation,
  messageRefus,
  phraseTrouvee,
  refusLocal,
} from '../reference/localiser'
import type { CorpsLocalisation, ResultatLocalisation } from '../reference/localiser'
import type { ChampRessource, LocalisationRessource } from '../types/reference'
import './FormulaireRessource.css'

/** Une option de liste déroulante pour un champ de référence. */
export interface OptionReference {
  valeur: string
  libelle: string
  /** `false` pour une ligne désactivée ; absent pour une ressource qui n'en a pas. */
  actif?: boolean
  /** La ligne entière, pour les champs qui `preremplit`. */
  ligne?: Ligne
}

interface Proprietes {
  titre: string
  champs: readonly ChampRessource[]

  /**
   * Valeurs de départ. Lues **une seule fois**, au montage.
   *
   * ⚠️ La page remonte ce composant par une `key` quand la ligne modifiée change.
   * Synchroniser un état interne sur une propriété par un effet est le défaut que
   * signale `react(set-state-in-effect)`, et il s'est déjà payé une fois à l'étape
   * 14 : un rendu de plus, et un état qui se désynchronise.
   */
  depart: ValeursFormulaire

  /** Options des champs de type `reference`, par nom de champ. */
  options: Record<string, readonly OptionReference[]>

  /**
   * Choix tronqué : la liste déroulante d'un champ de référence n'a reçu qu'une
   * page. Dire que le choix est incomplet vaut mieux que laisser conclure que la
   * ligne cherchée n'existe pas.
   */
  optionsTronquees: Record<string, boolean>

  /**
   * Lecture seule. Porte la décision de l'étape : un `Responsable` **lit** les
   * données de référence sans y écrire, et afficher un bouton d'enregistrement qui
   * répondrait 403 est une faute d'interface, pas une protection. La garde reste au
   * serveur ; l'écran ne fait que ne pas mentir.
   */
  lectureSeule: boolean

  enCours: boolean
  erreur: string | null

  onEnregistrer: (valeurs: ValeursFormulaire) => void
  onAnnuler: () => void

  /**
   * « Localiser » (0.3.0), offert quand l'entreprise a coché le réglage. La page appelle, le formulaire
   * tient les valeurs et pose les coordonnées. Absent : aucun bouton.
   */
  localisation?: {
    champs: LocalisationRessource
    onLocaliser: (corps: CorpsLocalisation, signal: AbortSignal) => Promise<ResultatLocalisation>
  }
}

type EtatLocalisation =
  | { etat: 'repos' }
  | { etat: 'enCours' }
  | { etat: 'trouvee'; resultat: ResultatLocalisation }
  | { etat: 'refusee'; message: string }

export function FormulaireRessource({
  titre,
  champs,
  depart,
  options,
  optionsTronquees,
  lectureSeule,
  enCours,
  erreur,
  onEnregistrer,
  onAnnuler,
  localisation,
}: Proprietes) {
  const [valeurs, setValeurs] = useState<ValeursFormulaire>(depart)
  const [localisationEtat, setLocalisationEtat] = useState<EtatLocalisation>({ etat: 'repos' })
  // Chaque modification à la main d'un champ de localisation invalide la réponse attendue : la ligne
  // « Trouvé » ne ment jamais sur des coordonnées qui n'en viennent plus.
  const generation = useRef(0)
  const controleurLocalisation = useRef<AbortController | null>(null)

  useEffect(() => () => controleurLocalisation.current?.abort(), [])

  const nomsLocalisation: string[] = localisation === undefined ? [] : Object.values(localisation.champs)

  function changer(nom: string, valeur: ValeursFormulaire[string]) {
    if (nomsLocalisation.includes(nom)) {
      generation.current += 1
      setLocalisationEtat({ etat: 'repos' })
    }

    const champ = champs.find((candidat) => candidat.nom === nom)

    setValeurs((precedentes) => {
      const suivantes = { ...precedentes, [nom]: valeur }

      if (champ?.preremplit === undefined) return suivantes

      return preremplir(champ, suivantes, ligneDe(nom, precedentes[nom]), ligneDe(nom, valeur))
    })
  }

  async function localiserSite() {
    if (localisation === undefined || localisationEtat.etat === 'enCours') return

    const noms = localisation.champs
    const texte = (nom: string) => (typeof valeurs[nom] === 'string' ? valeurs[nom] : '')
    const refus = refusLocal(texte(noms.adresse), texte(noms.codePostal), texte(noms.ville), bornesDe(champs, noms))

    if (refus !== null) {
      setLocalisationEtat({ etat: 'refusee', message: refus })
      document.getElementById(`champ-${noms.adresse}`)?.focus()
      return
    }

    const mienne = ++generation.current
    controleurLocalisation.current?.abort()
    const controleur = new AbortController()
    controleurLocalisation.current = controleur
    setLocalisationEtat({ etat: 'enCours' })

    try {
      const resultat = await localisation.onLocaliser(
        corpsLocalisation(texte(noms.adresse), texte(noms.codePostal), texte(noms.ville)),
        controleur.signal,
      )
      if (mienne !== generation.current) return

      // Par setValeurs directement, et non par changer : l'écriture de la localisation ne l'efface pas.
      setValeurs((precedentes) => ({
        ...precedentes,
        [noms.latitude]: chaineCoordonnee(resultat.latitude),
        [noms.longitude]: chaineCoordonnee(resultat.longitude),
      }))
      setLocalisationEtat({ etat: 'trouvee', resultat })
    } catch (echec) {
      if (mienne !== generation.current) return
      const message = messageRefus(echec)
      setLocalisationEtat(message === null ? { etat: 'repos' } : { etat: 'refusee', message })
    }
  }

  function ligneDe(nom: string, valeur: ValeursFormulaire[string] | undefined): Ligne | undefined {
    return options[nom]?.find((option) => option.valeur === valeur)?.ligne
  }

  return (
    <form
      className="formulaire"
      onSubmit={(evenement) => {
        evenement.preventDefault()
        if (!lectureSeule && !enCours) onEnregistrer(valeurs)
      }}
      // aria-label plutôt qu'un titre accessible implicite : le formulaire est un
      // panneau latéral, un lecteur d'écran doit pouvoir l'annoncer en y entrant.
      aria-label={titre}
    >
      <header className="formulaire__entete">
        <h2 className="formulaire__titre">{titre}</h2>
        <button
          className="formulaire__fermer"
          type="button"
          onClick={onAnnuler}
          aria-label="Fermer le formulaire"
        >
          <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
            <path
              d="M6 6l12 12M18 6L6 18"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
            />
          </svg>
        </button>
      </header>

      {lectureSeule && (
        <p className="formulaire__lecture-seule">
          <strong>Lecture seule.</strong> La modification des données de référence est
          réservée à l’administrateur.
        </p>
      )}

      {erreur !== null && (
        <p className="formulaire__erreur" role="alert">
          <strong className="formulaire__erreur-mot">Refusé —</strong> {erreur}
        </p>
      )}

      <div className="formulaire__champs">
        {/* ⚠️ Un champ masqué n'est pas désactivé, il n'est **pas rendu**. Le motif
            d'une absence `Maladie` est une donnée de santé (article 9 du RGPD) : un
            champ grisé mais présent reste lisible, copiable, et conserve ce qui y
            avait été écrit avant le basculement. `corpsDe` envoie `null` de son côté,
            donc rien ne part — les deux moitiés sont nécessaires, et seule la seconde
            est éprouvable. */}
        {champs
          .filter((champ) => !masque(champ, valeurs))
          .map((champ) => (
            <Fragment key={champ.nom}>
              <Champ
                champ={champ}
                valeur={valeurs[champ.nom] ?? ''}
                options={optionsProposees(champ, options[champ.nom] ?? [], depart[champ.nom])}
                tronquee={optionsTronquees[champ.nom] === true}
                lectureSeule={lectureSeule}
                onChanger={changer}
              />
              {localisation !== undefined && !lectureSeule && champ.nom === localisation.champs.longitude && (
                <Localisation etat={localisationEtat} onLocaliser={() => void localiserSite()} />
              )}
            </Fragment>
          ))}
      </div>

      {/* Aucun bouton d'enregistrement en lecture seule : c'est la décision de
          l'étape, et c'est la seule façon de ne pas promettre un 403. */}
      {!lectureSeule && (
        <div className="formulaire__actions">
          <button className="formulaire__valider" type="submit" disabled={enCours}>
            {enCours ? 'Enregistrement…' : 'Enregistrer'}
          </button>
          <button
            className="formulaire__annuler"
            type="button"
            onClick={onAnnuler}
            disabled={enCours}
          >
            Annuler
          </button>
        </div>
      )}
    </form>
  )
}

interface ProprietesChamp {
  champ: ChampRessource
  valeur: ValeursFormulaire[string]
  options: readonly OptionReference[]
  tronquee: boolean
  lectureSeule: boolean
  onChanger: (nom: string, valeur: ValeursFormulaire[string]) => void
}

function Champ({ champ, valeur, options, tronquee, lectureSeule, onChanger }: ProprietesChamp) {
  const identifiant = `champ-${champ.nom}`
  const identifiantAide = `${identifiant}-aide`
  const aide = champ.aide

  if (champ.type === 'jours') {
    return (
      <ChampJours
        champ={champ}
        coches={Array.isArray(valeur) ? valeur : []}
        lectureSeule={lectureSeule}
        onChanger={onChanger}
      />
    )
  }

  const commun = {
    id: identifiant,
    name: champ.nom,
    required: champ.requis === true,
    disabled: lectureSeule,
    'aria-describedby': aide === undefined ? undefined : identifiantAide,
  }

  /**
   * ⚠️ `maxLength` n'est pas décoratif, et il est à part : sans lui, un dépassement
   * de longueur est refusé par la validation de modèle d'ASP.NET, qui s'exécute
   * avant le contrôleur et répond **en anglais** — « The field Nom must be a string
   * or array type with a maximum length of '200' », un texte que l'écran ne peut
   * pas traduire puisqu'il traverse l'API tel quel (CLAUDE.md §6). Le navigateur,
   * lui, empêche la saisie, et refuse dans sa propre langue.
   *
   * Séparé de `commun` parce que l'attribut n'existe ni sur `<select>` ni sur une
   * case à cocher : le compilateur le refuse, et il a raison.
   */
  const longueur = { maxLength: champ.longueurMax }

  return (
    <div className={champ.type === 'booleen' ? 'champ champ--case' : 'champ'}>
      <label className="champ__libelle" htmlFor={identifiant}>
        {champ.libelle}
        {/* Le mot, pas l'astérisque : un astérisque est une convention qu'il faut
            connaître, et la couleur seule ne porte jamais d'information (§12). */}
        {champ.requis === true && <span className="champ__requis"> (obligatoire)</span>}
      </label>

      {champ.type === 'texteLong' && (
        <textarea
          {...commun}
          {...longueur}
          className="champ__zone"
          rows={4}
          value={typeof valeur === 'string' ? valeur : ''}
          onChange={(evenement) => {
            onChanger(champ.nom, evenement.target.value)
          }}
        />
      )}

      {champ.type === 'choix' && (
        <select
          {...commun}
          className="champ__liste"
          value={typeof valeur === 'string' ? valeur : ''}
          onChange={(evenement) => {
            onChanger(champ.nom, evenement.target.value)
          }}
        >
          <option value="">— choisir —</option>
          {(champ.choix ?? []).map((choix) => (
            // Le mot à l'écran, le nom exact de l'énumération à l'envoi.
            <option key={choix} value={choix}>
              {champ.libelles?.[choix] ?? choix}
            </option>
          ))}
        </select>
      )}

      {(champ.type === 'reference' || champ.type === 'compte') && (
        <select
          {...commun}
          className="champ__liste"
          value={typeof valeur === 'string' ? valeur : ''}
          onChange={(evenement) => {
            onChanger(champ.nom, evenement.target.value)
          }}
        >
          <option value="">— choisir —</option>
          {options.map((option) => (
            <option key={option.valeur} value={option.valeur}>
              {option.libelle}
            </option>
          ))}
          {/* Un compte rattaché que la liste ne contient pas : celle-ci n'est chargée
              que pour qui écrit. Sans cette option, un lecteur verrait « — choisir — »
              et croirait l'agent sans chef. */}
          {champ.type === 'compte'
            && typeof valeur === 'string'
            && valeur !== ''
            && !options.some((option) => option.valeur === valeur) && (
            <option value={valeur}>Rattaché — nom non communiqué</option>
          )}
        </select>
      )}

      {champ.type === 'booleen' && (
        <input
          {...commun}
          className="champ__case"
          type="checkbox"
          // `required` sur une case à cocher obligerait à la cocher, ce qui n'est
          // pas ce que « requis » veut dire pour un booléen.
          required={false}
          checked={valeur === true}
          onChange={(evenement) => {
            onChanger(champ.nom, evenement.target.checked)
          }}
        />
      )}

      {(champ.type === 'texte'
        || champ.type === 'courriel'
        || champ.type === 'telephone'
        || champ.type === 'nombre'
        || champ.type === 'date'
        || champ.type === 'heure') && (
        <input
          {...commun}
          {...longueur}
          className="champ__saisie"
          type={typeDeSaisie(champ.type)}
          inputMode={champ.type === 'telephone' ? 'tel' : undefined}
          min={champ.min}
          max={champ.max}
          step={champ.pas}
          value={typeof valeur === 'string' ? valeur : ''}
          onChange={(evenement) => {
            onChanger(champ.nom, evenement.target.value)
          }}
        />
      )}

      {aide !== undefined && (
        <p className="champ__aide" id={identifiantAide}>
          {aide}
        </p>
      )}

      {tronquee && (
        <p className="champ__avertissement">
          Seules les 200 premières lignes sont proposées. Au-delà, créer la ligne
          manquante depuis son propre écran.
        </p>
      )}
    </div>
  )
}

/**
 * Les jours de passage : un groupe de cases, donc un `fieldset` et sa légende — une
 * étiquette unique ne peut pas nommer sept contrôles, et un lecteur d'écran annonce
 * la légende en entrant dans le groupe.
 */
function ChampJours({
  champ,
  coches,
  lectureSeule,
  onChanger,
}: {
  champ: ChampRessource
  coches: readonly string[]
  lectureSeule: boolean
  onChanger: (nom: string, valeur: ValeursFormulaire[string]) => void
}) {
  const identifiantAide = `champ-${champ.nom}-aide`

  return (
    <fieldset
      className="champ champ--jours"
      aria-describedby={champ.aide === undefined ? undefined : identifiantAide}
    >
      <legend className="champ__libelle">{champ.libelle}</legend>

      <div className="champ__jours">
        {(champ.choix ?? []).map((jour) => (
          <label key={jour} className="champ__jour">
            <input
              className="champ__case"
              type="checkbox"
              name={champ.nom}
              value={jour}
              disabled={lectureSeule}
              checked={coches.includes(jour)}
              onChange={(evenement) => {
                onChanger(
                  champ.nom,
                  evenement.target.checked
                    ? [...coches, jour]
                    : coches.filter((coche) => coche !== jour),
                )
              }}
            />
            {jour}
          </label>
        ))}
      </div>

      {champ.aide !== undefined && (
        <p className="champ__aide" id={identifiantAide}>
          {champ.aide}
        </p>
      )}
    </fieldset>
  )
}

function typeDeSaisie(type: ChampRessource['type']): string {
  if (type === 'courriel') return 'email'
  if (type === 'telephone') return 'tel'
  if (type === 'nombre') return 'number'
  if (type === 'date') return 'date'
  if (type === 'heure') return 'time'

  return 'text'
}

interface ProprietesLocalisation {
  etat: EtatLocalisation
  onLocaliser: () => void
}

/**
 * Le bouton et le résultat. ⚠️ Jamais `disabled` pendant l'appel : le navigateur ferait perdre le focus
 * au bouton. Le résultat vit dans un `<output>` toujours rendu — un conteneur vivant doit exister avant
 * que son contenu change, sinon le lecteur d'écran ne l'annonce pas.
 */
function Localisation({ etat, onLocaliser }: ProprietesLocalisation) {
  const enCours = etat.etat === 'enCours'

  return (
    <div className="localisation">
      <button
        className="localisation__bouton"
        type="button"
        aria-disabled={enCours}
        aria-describedby="localisation-aide"
        onClick={onLocaliser}
      >
        {enCours ? 'Localisation…' : 'Localiser'}
      </button>

      <p className="champ__aide" id="localisation-aide">
        {AideLocaliser}
      </p>

      <output className="localisation__resultat">
        {etat.etat === 'trouvee' && (
          <>
            {phraseTrouvee(etat.resultat)}
            {etat.resultat.aVerifier && (
              <>
                {' — '}
                <strong className="localisation__a-verifier">{MotAVerifier}</strong>
              </>
            )}
          </>
        )}
      </output>

      {etat.etat === 'refusee' && (
        <p className="formulaire__erreur" role="alert">
          <strong className="formulaire__erreur-mot">Refusé —</strong> {etat.message}
        </p>
      )}
    </div>
  )
}
