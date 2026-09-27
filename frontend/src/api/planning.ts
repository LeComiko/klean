import { api } from './client'
import type { PlanningSemaine } from '../types/planning'

/**
 * Planning d'une semaine. `lundi` est au format `AAAA-MM-JJ` et doit tomber un
 * lundi : le serveur refuse en 400 sinon, avec le nom du jour en français.
 *
 * Aucune valeur par défaut calculée ici non plus. Le serveur n'en a pas voulu
 * pour ne pas dépendre de son horloge ; l'écran sait toujours quelle semaine il
 * affiche, et c'est lui qui la nomme.
 */
export function getPlanningSemaine(
  lundi: string,
  signal?: AbortSignal,
): Promise<PlanningSemaine> {
  return api.get<PlanningSemaine>(`/api/planning?lundi=${lundi}`, { signal })
}
