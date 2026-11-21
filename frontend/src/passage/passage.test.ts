import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'

import type { PointChecklist, Pointage, VacationAgent } from '../types/passage.ts'
import {
  ConsignePhoto,
  LibellesStatut,
  LongueurMaxDescription,
  NoticeLocalisation,
  TailleMaxPhoto,
  bilanDe,
  dimensionsReduites,
  formaterDistance,
  gesteDe,
  heureDe,
  localiser,
  phrasePreuve,
  photosRestantes,
  pointsFaits,
  pointsNonCoches,
  refusDescription,
  refusFichier,
  resumeChecklist,
  statutDuFiltre,
} from './passage.ts'

/**
 * Ce que les écrans du passage décident sans le serveur (SPEC.md, étape 25, lot C).
 */

function pointage(preuve: Pointage['preuve'], distanceMetres: number | null = 40): Pointage {
  return { horodatage: '2026-11-06T07:05:00Z', preuve, distanceMetres, precisionMetres: 12 }
}

function vacation(partiel: Partial<VacationAgent>): VacationAgent {
  return {
    id: 'v1',
    datePrevue: '2026-11-06T00:00:00Z',
    heurePrevue: '08:00:00',
    dureeEstimeeMinutes: 90,
    statut: 'Planifiee',
    prestation: 'Hall d’entrée',
    site: 'Résidence Bellevue',
    adresse: '3 rue des Lilas',
    codePostal: null,
    ville: null,
    consignes: null,
    debut: null,
    fin: null,
    checklist: [],
    anomalies: [],
    ...partiel,
  }
}

const Checklist: PointChecklist[] = [
  { pointControleId: 'a', libelle: 'Vitres du hall', fait: null },
  { pointControleId: 'b', libelle: 'Poubelles sorties', fait: null },
  // Un point supprimé depuis la déclaration : il n'a plus d'identifiant.
  { pointControleId: null, libelle: 'Ancien point', fait: true },
]

describe('passage — le geste proposé', () => {
  test('démarrer une vacation à faire, terminer une vacation en cours', () => {
    assert.equal(gesteDe('Planifiee'), 'Demarrer')
    assert.equal(gesteDe('EnCours'), 'Terminer')
  })

  test('aucun geste sur une vacation terminée, non certifiée ou annulée', () => {
    for (const statut of ['Terminee', 'NonCertifiee', 'Annulee'] as const) {
      assert.equal(gesteDe(statut), 'Aucun', statut)
    }
  })
})

describe('passage — ce que dit un pointage', () => {
  test('les distances, en mètres puis en kilomètres à la virgule', () => {
    assert.equal(formaterDistance(35), '35 m')
    assert.equal(formaterDistance(999.6), '1000 m')
    assert.equal(formaterDistance(1240), '1,2 km')
  })

  test('une phrase par preuve, et la distance quand il y en a une', () => {
    assert.equal(phrasePreuve(pointage('SurSite', 35)), 'Sur place, à 35 m du site')
    assert.equal(phrasePreuve(pointage('HorsRayon', 1240)), 'Hors du rayon de 200 m, à 1,2 km du site')
    assert.match(phrasePreuve({ ...pointage('Imprecise'), precisionMetres: 250 }), /imprécise.*± 250 m/)
    assert.equal(phrasePreuve(pointage('NonLocalisee', null)), 'Sans position')
  })

  test('l’heure d’un pointage est l’heure locale de l’instant', () => {
    // Construit en heure locale pour ne dépendre d'aucun fuseau.
    const instant = new Date(2026, 10, 6, 8, 5).toISOString()

    assert.equal(heureDe(instant), '08:05')
  })
})

describe('passage — le bilan', () => {
  test('rien tant que le passage n’est pas terminé', () => {
    assert.equal(bilanDe(vacation({ statut: 'Planifiee' })), null)
    assert.equal(bilanDe(vacation({ statut: 'EnCours', debut: pointage('SurSite') })), null)
  })

  test('certifié : les deux pointages sur place', () => {
    assert.match(
      bilanDe(vacation({ statut: 'Terminee', debut: pointage('SurSite'), fin: pointage('SurSite') })) ?? '',
      /^Passage certifié/,
    )
  })

  test('non certifié : le bilan nomme chaque pointage en cause', () => {
    // « Non certifié » seul laisserait croire à une panne, alors que l'agent a peut-être
    // refusé la localisation.
    const bilan = bilanDe(vacation({
      statut: 'NonCertifiee',
      debut: pointage('NonLocalisee', null),
      fin: pointage('HorsRayon', 900),
    }))

    assert.equal(bilan, 'Passage terminé, non certifié : le début sans position, la fin hors du rayon.')
  })

  test('un pointage sur place n’est pas cité comme cause', () => {
    const bilan = bilanDe(vacation({
      statut: 'NonCertifiee',
      debut: pointage('SurSite'),
      fin: pointage('Imprecise'),
    }))

    assert.equal(bilan, 'Passage terminé, non certifié : la fin avec une position imprécise.')
  })

  test('une vacation saisie « faite, sans preuve » le dit, sans inventer de pointage', () => {
    assert.match(bilanDe(vacation({ statut: 'NonCertifiee' })) ?? '', /sans pointage/)
  })

  test('jamais « certifié » sur un passage qui ne l’est pas', () => {
    for (const statut of ['NonCertifiee', 'Annulee', 'EnCours', 'Planifiee'] as const) {
      assert.doesNotMatch(bilanDe(vacation({ statut })) ?? '', /^Passage certifié/, statut)
    }
  })
})

describe('passage — la checklist', () => {
  test('seuls les points cochés de cette checklist partent', () => {
    // ⚠️ Le serveur refuse toute la terminaison sur un identifiant inconnu : une coche
    // restée d'une autre vacation suffirait à bloquer l'agent.
    assert.deepEqual(pointsFaits(Checklist, new Set(['b', 'autre-vacation'])), ['b'])
  })

  test('un point sans identifiant ne part jamais', () => {
    assert.deepEqual(pointsFaits(Checklist, new Set(['a', 'b'])), ['a', 'b'])
  })

  test('la fin se confirme tant qu’un point reste à cocher', () => {
    assert.equal(pointsNonCoches(Checklist, new Set(['a'])), 1)
    assert.equal(pointsNonCoches(Checklist, new Set(['a', 'b'])), 0)
    // Le point supprimé n'est pas cochable : il ne retient pas la fin.
    assert.equal(pointsNonCoches([Checklist[2]!], new Set()), 0)
    // Une coche étrangère ne compte pas pour un point de cette checklist.
    assert.equal(pointsNonCoches(Checklist, new Set(['a', 'autre-vacation'])), 1)
  })

  test('le résumé compte les coches sur le total', () => {
    assert.equal(resumeChecklist(Checklist, new Set(['a'])), '1 point sur 3 coché')
    assert.equal(resumeChecklist(Checklist, new Set(['a', 'b'])), '2 points sur 3 cochés')
    assert.match(resumeChecklist([], new Set()), /Aucun point de contrôle/)
  })
})

describe('passage — l’anomalie et sa photo', () => {
  test('une description vide ou trop longue est refusée en français', () => {
    assert.match(refusDescription('   ') ?? '', /^Décrire l’anomalie/)
    assert.match(refusDescription('x'.repeat(LongueurMaxDescription + 1)) ?? '', /1000 caractères/)
    assert.equal(refusDescription('x'.repeat(LongueurMaxDescription)), null)
    // Le serveur compte après `Trim()` : les espaces de bord ne comptent pas.
    assert.equal(refusDescription(`  ${'x'.repeat(LongueurMaxDescription)}  `), null)
  })

  test('trois photos au plus par anomalie', () => {
    assert.equal(photosRestantes({ id: 'a', description: 'd', photos: 0 }), 3)
    assert.equal(photosRestantes({ id: 'a', description: 'd', photos: 3 }), 0)
    assert.equal(photosRestantes({ id: 'a', description: 'd', photos: 5 }), 0)
  })

  test('un fichier qui n’est pas une image est refusé', () => {
    assert.equal(refusFichier({ type: 'image/heic' }), null)
    assert.equal(refusFichier({ type: 'application/pdf' }), 'Choisir une photo.')
  })

  test('la réduction ramène le plus grand côté, sans jamais agrandir', () => {
    assert.deepEqual(dimensionsReduites(4032, 3024, 1600), { largeur: 1600, hauteur: 1200 })
    assert.deepEqual(dimensionsReduites(3024, 4032, 1600), { largeur: 1200, hauteur: 1600 })
    assert.deepEqual(dimensionsReduites(800, 600, 1600), { largeur: 800, hauteur: 600 })
  })

  test('la photo réduite tient sous le plafond de corps de l’API', () => {
    // Au-delà d'un mébioctet, la requête est refusée en 413 avant le contrôleur.
    assert.ok(TailleMaxPhoto < 1024 * 1024)
  })
})

describe('passage — la position', () => {
  function geolocalisation(
    agir: (succes: PositionCallback, echec: PositionErrorCallback, options?: PositionOptions) => void,
  ): Pick<Geolocation, 'getCurrentPosition'> {
    return {
      getCurrentPosition: (succes, echec, options) => {
        agir(succes, echec ?? (() => undefined), options)
      },
    }
  }

  test('trois nombres, et rien d’autre de ce que rend le navigateur', async () => {
    const position = await localiser(geolocalisation((succes) => {
      succes({
        coords: { latitude: 48.85, longitude: 2.35, accuracy: 14, altitude: 35, heading: 90, speed: 1 },
        timestamp: 0,
      } as unknown as GeolocationPosition)
    }))

    assert.deepEqual(position, { latitude: 48.85, longitude: 2.35, precisionMetres: 14 })
  })

  test('un refus donne un passage sans position, pas un échec', async () => {
    const position = await localiser(geolocalisation((_succes, echec) => {
      echec({ code: 1, message: 'User denied Geolocation' } as GeolocationPositionError)
    }))

    assert.equal(position, null)
  })

  test('un téléphone sans géolocalisation aussi', async () => {
    assert.equal(await localiser(undefined), null)
  })

  test('une position fraîche, jamais celle du cache', async () => {
    // Une position d'avant le trajet certifierait un passage depuis le dépôt.
    let demandees: PositionOptions | undefined

    await localiser(geolocalisation((_succes, echec, options) => {
      demandees = options
      echec({ code: 3, message: 'Timeout' } as GeolocationPositionError)
    }))

    assert.equal(demandees?.maximumAge, 0)
    assert.ok((demandees?.timeout ?? Infinity) < Infinity)
  })

  test('la notice dit que la position n’est pas gardée', () => {
    assert.match(NoticeLocalisation, /jamais\s+la position/)
  })

  // ⚠️ Une phrase d'aide retirée ne casse rien de visible : c'est pour cela qu'elle a
  // deux gardes, son texte et sa présence sur l'écran (RGPD.md §9.9).
  test('la consigne photo dit de ne photographier personne', () => {
    assert.match(ConsignePhoto, /jamais une personne/)
  })

  test('Ma journée affiche la consigne photo', () => {
    const ecran = readFileSync(new URL('../pages/MaJournee.tsx', import.meta.url), 'utf8')

    assert.match(ecran, /\{ConsignePhoto\}/)
  })
})

describe('anomalies — le filtre de la liste', () => {
  test('« Toutes » n’envoie aucun statut, les autres leur nom exact', () => {
    assert.equal(statutDuFiltre('Toutes'), null)
    assert.equal(statutDuFiltre('Ouverte'), 'Ouverte')
    assert.equal(statutDuFiltre('Traitee'), 'Traitee')
  })
})

describe('les statuts d’une vacation — un nom, sur les deux écrans', () => {
  /** `StatutIntervention`, lu dans le source du serveur. */
  function statutsDuServeur(): string[] {
    const source = readFileSync(new URL('../../../backend/Models/Intervention.cs', import.meta.url), 'utf8')
    const bloc = /public enum StatutIntervention\s*\{([\s\S]*?)\n\}/.exec(source)
    assert.ok(bloc, 'énumération StatutIntervention introuvable')

    return (bloc[1] ?? '')
      .split('\n')
      .map((ligne) => /^\s*([A-Z]\w*),?\s*$/.exec(ligne)?.[1])
      .filter((nom): nom is string => nom !== undefined)
  }

  test('chaque statut du serveur a son libellé, et aucun n’est son nom brut', () => {
    assert.deepEqual(Object.keys(LibellesStatut), statutsDuServeur())

    for (const [nom, libelle] of Object.entries(LibellesStatut)) assert.notEqual(libelle, nom)
  })

  // Le défaut qu'a montré le jeu de démonstration (étape 28) : le bloc écrivait `{statut}`.
  test('le bloc du planning écrit le libellé, pas le statut brut', () => {
    const bloc = readFileSync(new URL('../composants/BlocVacation.tsx', import.meta.url), 'utf8')

    assert.match(bloc, /\{LibellesStatut\[statut\]\}/)
    assert.doesNotMatch(bloc, />\{statut\}</)
  })
})
