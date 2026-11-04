import type { FormEvent } from 'react'

import { AidesNature, LibellesNature, lieuxRequis, Natures } from '../stock/mouvements'
import type { SaisieMouvement } from '../stock/mouvements'
import type { NatureMouvement } from '../types/stock'
import './FormulaireRessource.css'
import './FormulaireMouvement.css'

/** Une option de liste : sa valeur, son libellé, et si elle peut recevoir. */
export interface OptionLieu {
  valeur: string
  libelle: string
  /** Faux pour un emplacement désactivé : il ne reçoit plus rien, mais peut se vider. */
  recoit: boolean
}

export interface OptionProduit {
  valeur: string
  libelle: string
  unite: string
  actif: boolean
}

interface Proprietes {
  saisie: SaisieMouvement
  produits: readonly OptionProduit[]
  emplacements: readonly OptionLieu[]
  sites: readonly OptionLieu[]
  enCours: boolean
  erreur: string | null
  onChanger: (saisie: SaisieMouvement) => void
  onEnregistrer: () => void
  onAnnuler: () => void
}

/**
 * La saisie d'un mouvement de stock. Composant de présentation : la page tient l'état,
 * appelle le serveur et prononce les refus.
 *
 * Seuls les lieux qu'exige la nature sont rendus. Ceux qu'elle n'exige pas restent dans
 * l'état, et `corpsMouvement` les envoie à `null` — les deux moitiés sont nécessaires,
 * et seule la seconde est éprouvable (CLAUDE.md §10).
 */
export function FormulaireMouvement({
  saisie,
  produits,
  emplacements,
  sites,
  enCours,
  erreur,
  onChanger,
  onEnregistrer,
  onAnnuler,
}: Proprietes) {
  const requis = lieuxRequis(saisie.nature)
  const inventaire = saisie.nature === 'Inventaire'
  // L'entrée et le transfert font recevoir la destination ; un inventaire la compte.
  const recoit = saisie.nature === 'Entree' || saisie.nature === 'Transfert'
  const unite = produits.find((produit) => produit.valeur === saisie.produitId)?.unite

  function changer(partielle: Partial<SaisieMouvement>) {
    onChanger({ ...saisie, ...partielle })
  }

  function soumettre(evenement: FormEvent<HTMLFormElement>) {
    evenement.preventDefault()
    onEnregistrer()
  }

  return (
    <form className="formulaire" onSubmit={soumettre} aria-labelledby="titre-mouvement">
      <div className="formulaire__entete">
        <h2 className="formulaire__titre" id="titre-mouvement">
          Nouveau mouvement
        </h2>
        <button className="formulaire__fermer" type="button" aria-label="Fermer" onClick={onAnnuler}>
          ×
        </button>
      </div>

      {erreur !== null && (
        <p className="formulaire__erreur" role="alert">
          <strong className="formulaire__erreur-mot">Refusé —</strong> {erreur}
        </p>
      )}

      <div className="formulaire__champs">
        <div className="champ">
          <label className="champ__libelle" htmlFor="mouvement-nature">
            Nature
          </label>
          <select
            id="mouvement-nature"
            className="champ__liste"
            value={saisie.nature}
            aria-describedby="mouvement-nature-aide"
            onChange={(evenement) => {
              changer({ nature: evenement.target.value as NatureMouvement })
            }}
          >
            {Natures.map((nature) => (
              <option key={nature} value={nature}>
                {LibellesNature[nature]}
              </option>
            ))}
          </select>
          <p className="champ__aide" id="mouvement-nature-aide">
            {AidesNature[saisie.nature]}
          </p>
        </div>

        <div className="champ">
          <label className="champ__libelle" htmlFor="mouvement-produit">
            Produit <span className="champ__requis">(obligatoire)</span>
          </label>
          <select
            id="mouvement-produit"
            className="champ__liste"
            required
            value={saisie.produitId}
            onChange={(evenement) => {
              changer({ produitId: evenement.target.value })
            }}
          >
            <option value="">— choisir —</option>
            {produits.map((produit) => (
              // Un produit désactivé reste proposé : ce qui en reste peut sortir et se
              // compter. Le mot dit pourquoi une entrée serait refusée.
              <option key={produit.valeur} value={produit.valeur}>
                {produit.libelle}
                {produit.actif ? '' : ' (désactivé)'}
              </option>
            ))}
          </select>
        </div>

        {requis.origine && (
          <ChoixLieu
            id="mouvement-origine"
            libelle="Depuis"
            valeur={saisie.origine}
            emplacements={emplacements}
            sites={sites}
            seulementQuiRecoivent={false}
            onChanger={(origine) => {
              changer({ origine })
            }}
          />
        )}

        {requis.destination && (
          <ChoixLieu
            id="mouvement-destination"
            libelle={inventaire ? 'Lieu compté' : 'Vers'}
            valeur={saisie.destination}
            emplacements={emplacements}
            sites={sites}
            seulementQuiRecoivent={recoit}
            onChanger={(destination) => {
              changer({ destination })
            }}
          />
        )}

        <div className="champ">
          <label className="champ__libelle" htmlFor="mouvement-quantite">
            {inventaire ? 'Quantité comptée' : 'Quantité'}
            {unite !== undefined && ` (${unite})`} <span className="champ__requis">(obligatoire)</span>
          </label>
          <input
            id="mouvement-quantite"
            className="champ__saisie champ__saisie--chiffres"
            type="number"
            required
            // Les bornes du DTO, que le serveur ferait respecter en anglais.
            min={inventaire ? 0 : 0.01}
            max={99999999.99}
            step={0.01}
            inputMode="decimal"
            value={saisie.quantite}
            onChange={(evenement) => {
              changer({ quantite: evenement.target.value })
            }}
          />
          {inventaire && (
            <p className="champ__aide">Zéro constate un lieu vide.</p>
          )}
        </div>
      </div>

      <div className="formulaire__actions">
        <button className="formulaire__valider" type="submit" disabled={enCours}>
          {enCours ? 'Enregistrement…' : 'Enregistrer'}
        </button>
        <button className="formulaire__annuler" type="button" onClick={onAnnuler} disabled={enCours}>
          Fermer
        </button>
      </div>
    </form>
  )
}

interface ProprietesLieu {
  id: string
  libelle: string
  valeur: string
  emplacements: readonly OptionLieu[]
  sites: readonly OptionLieu[]
  /** Vrai quand le lieu reçoit : un emplacement désactivé n'est alors pas proposé. */
  seulementQuiRecoivent: boolean
  onChanger: (valeur: string) => void
}

/**
 * Un lieu, dépôts et véhicules d'un côté, sites de l'autre. Une seule liste, parce
 * qu'on choisit un **endroit** ; deux groupes, parce que ce ne sont pas les mêmes.
 */
function ChoixLieu({ id, libelle, valeur, emplacements, sites, seulementQuiRecoivent, onChanger }: ProprietesLieu) {
  const proposes = seulementQuiRecoivent
    ? emplacements.filter((emplacement) => emplacement.recoit || emplacement.valeur === valeur)
    : emplacements

  return (
    <div className="champ">
      <label className="champ__libelle" htmlFor={id}>
        {libelle} <span className="champ__requis">(obligatoire)</span>
      </label>
      <select
        id={id}
        className="champ__liste"
        required
        value={valeur}
        onChange={(evenement) => {
          onChanger(evenement.target.value)
        }}
      >
        <option value="">— choisir —</option>
        <optgroup label="Dépôts et véhicules">
          {proposes.map((emplacement) => (
            <option key={emplacement.valeur} value={emplacement.valeur}>
              {emplacement.libelle}
            </option>
          ))}
        </optgroup>
        <optgroup label="Sites">
          {sites.map((site) => (
            <option key={site.valeur} value={site.valeur}>
              {site.libelle}
            </option>
          ))}
        </optgroup>
      </select>
    </div>
  )
}
