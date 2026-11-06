import { useState } from 'react'

import { texteDesCodes } from '../auth/secondFacteur'
import './CodesRecuperation.css'

interface Proprietes {
  codes: readonly string[]
  /** Le libellé du bouton qui quitte l'écran : « Continuer », « Terminé ». */
  libelleSuite: string
  surSuite: () => void
}

/**
 * Les codes de récupération, montrés **une seule fois** : le serveur n'en garde que
 * l'empreinte (SPEC.md, étape 23).
 *
 * D'où la case à cocher avant de continuer. Ce n'est pas une formalité : quitter cet
 * écran sans les avoir rangés, c'est n'avoir plus, le jour où le téléphone est perdu,
 * qu'un administrateur pour s'en sortir — et s'il n'y en a pas d'autre, un script en
 * base.
 */
export function CodesRecuperation({ codes, libelleSuite, surSuite }: Proprietes) {
  const [ranges, setRanges] = useState(false)
  const [copie, setCopie] = useState<'aucune' | 'faite' | 'impossible'>('aucune')

  async function copier() {
    try {
      await navigator.clipboard.writeText(texteDesCodes(codes))
      setCopie('faite')
    } catch {
      // Presse-papiers refusé par le navigateur : les codes restent à l'écran, à
      // recopier à la main. Le dire plutôt que laisser croire à une copie.
      setCopie('impossible')
    }
  }

  return (
    <div className="codes-recuperation">
      <p className="codes-recuperation__consigne">
        Chacun de ces codes ouvre une session <strong>une seule fois</strong>, à la place du
        code de l’application, si le téléphone est perdu. Ils ne seront plus jamais affichés.
      </p>

      <ol className="codes-recuperation__liste" aria-label="Codes de récupération">
        {codes.map((code) => (
          <li key={code} className="codes-recuperation__code">
            {code}
          </li>
        ))}
      </ol>

      <div className="codes-recuperation__actions">
        <button className="codes-recuperation__copier" type="button" onClick={() => void copier()}>
          Copier les codes
        </button>

        {copie === 'faite' && <output className="codes-recuperation__etat">Copiés.</output>}
        {copie === 'impossible' && (
          <output className="codes-recuperation__etat">
            Copie refusée par le navigateur : les recopier à la main.
          </output>
        )}
      </div>

      <label className="codes-recuperation__accord">
        <input
          type="checkbox"
          checked={ranges}
          onChange={(evenement) => setRanges(evenement.target.checked)}
        />
        J’ai rangé ces codes en lieu sûr, hors de ce téléphone.
      </label>

      <button className="codes-recuperation__suite" type="button" disabled={!ranges} onClick={surSuite}>
        {libelleSuite}
      </button>
    </div>
  )
}
