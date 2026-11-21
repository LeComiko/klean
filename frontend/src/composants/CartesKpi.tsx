import { formaterHeures } from '../utils/dates'
import './CartesKpi.css'

interface Proprietes {
  vacations: number
  heures: number
  nonPourvues: number
}

/**
 * Les trois agrégats de la semaine. Ils viennent du serveur et sont **recopiés**,
 * jamais recalculés à partir des lignes : deux additions indépendantes finissent
 * par diverger, et c'est alors le serveur qu'on accuse.
 */
export function CartesKpi({ vacations, heures, nonPourvues }: Proprietes) {
  const alerte = nonPourvues > 0

  return (
    <ul className="cartes-kpi">
      <li className="carte-kpi">
        <p className="carte-kpi__etiquette">Vacations</p>
        <p className="carte-kpi__valeur chiffres">{vacations}</p>
        {/* « au planning », pas « planifiées » : le total compte toute vacation non
            annulée, y compris celles déjà terminées d'une semaine passée. */}
        <p className="carte-kpi__detail">au planning cette semaine</p>
      </li>

      <li className="carte-kpi">
        <p className="carte-kpi__etiquette">Charge</p>
        <p className="carte-kpi__valeur chiffres">{formaterHeures(heures)}</p>
        <p className="carte-kpi__detail">durées estimées cumulées</p>
      </li>

      {/* La couleur ne porte pas seule l'information : le mot « à pourvoir » est
          écrit, et le trait pointillé en est la redondance non colorée. */}
      <li className={alerte ? 'carte-kpi carte-kpi--alerte' : 'carte-kpi'}>
        <p className="carte-kpi__etiquette">Non pourvues</p>
        <p className="carte-kpi__valeur chiffres">{nonPourvues}</p>
        <p className="carte-kpi__detail">
          {alerte ? 'à pourvoir' : 'tout est couvert'}
        </p>
      </li>
    </ul>
  )
}
