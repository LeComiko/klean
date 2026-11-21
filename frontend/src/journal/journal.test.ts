import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { describe, test } from 'node:test'

// ⚠️ Extensions explicites : `node --test` n'applique pas la résolution sans extension de
// Vite (CLAUDE.md §10).
import {
  auteurDe,
  cibleDe,
  FiltresVides,
  formaterInstant,
  identifiantCourt,
  libelleNature,
  libelleRessource,
  LibellesNature,
  LibellesRessource,
  Natures,
  periodeDe,
  refusDePeriode,
  Ressources,
  routeDuJournal,
} from './journal.ts'
import type { Annuaire } from './journal.ts'
import type { AccesJournalise } from '../types/journal.ts'

// ⚠️ Série rejouée dans cinq fuseaux (scripts/tests.mjs) : la période et l'heure affichée
// en dépendent, et aucune des deux erreurs possibles ne se voit sous TZ=UTC.

const Compte1 = '11111111-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const Agent1 = '22222222-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const Inconnu = '99999999-cccc-4ccc-8ccc-cccccccccccc'

const annuaire: Annuaire = {
  comptes: new Map([[Compte1, 'Claire Martin']]),
  agents: new Map([[Agent1, 'Paul Durand']]),
  incomplet: false,
}

function acces(modifs: Partial<AccesJournalise> = {}): AccesJournalise {
  return {
    id: 'a1',
    entrepriseId: 'e1',
    utilisateurId: Compte1,
    ressource: 'Agent',
    nature: 'Lecture',
    cibleId: Agent1,
    lignes: 1,
    horodatage: '2026-11-12T09:05:00Z',
    ...modifs,
  }
}

/** Les membres d'une énumération C#, lus dans le source du serveur. */
function membresDe(enumeration: string): string[] {
  const source = readFileSync(new URL('../../../backend/Models/AccesJournalise.cs', import.meta.url), 'utf8')
  const bloc = new RegExp(`public enum ${enumeration}\\s*\\{([\\s\\S]*?)\\n\\}`).exec(source)
  assert.ok(bloc, `énumération ${enumeration} introuvable`)

  return (bloc[1] ?? '')
    .split('\n')
    .map((ligne) => /^\s*([A-Z]\w*),?\s*$/.exec(ligne)?.[1])
    .filter((nom): nom is string => nom !== undefined)
}

describe('les libellés', () => {
  test('les ressources et les natures sont celles du serveur, dans son ordre', () => {
    assert.deepEqual([...Ressources], membresDe('RessourceAccedee'))
    assert.deepEqual([...Natures], membresDe('NatureAcces'))
  })

  test('chaque valeur a un libellé, et deux valeurs ne partagent pas le même', () => {
    for (const libelles of [Object.values(LibellesRessource), Object.values(LibellesNature)]) {
      assert.ok(libelles.every((libelle) => libelle.trim() !== ''))
      assert.equal(new Set(libelles).size, libelles.length)
    }
  })

  test('une anonymisation se lit pseudonymisation, ce qu’elle est', () => {
    assert.equal(libelleNature('Anonymisation'), 'Pseudonymisation')
  })

  test('un nom inconnu du serveur reste lisible tel quel', () => {
    assert.equal(libelleRessource('Nouvelle'), 'Nouvelle')
    assert.equal(libelleNature('Nouvelle'), 'Nouvelle')
  })
})

describe('l’auteur et la cible', () => {
  test('l’auteur est nommé par l’annuaire des comptes', () => {
    assert.equal(auteurDe(acces(), annuaire), 'Claire Martin')
  })

  test('un auteur introuvable rend son identifiant court, jamais un blanc', () => {
    assert.equal(auteurDe(acces({ utilisateurId: Inconnu }), annuaire), 'Compte 99999999')
    assert.equal(identifiantCourt(Inconnu), '99999999')
  })

  test('la fiche d’un agent est nommée ; introuvable, son identifiant court', () => {
    assert.equal(cibleDe(acces({ nature: 'Export' }), annuaire), 'Paul Durand')
    assert.equal(cibleDe(acces({ cibleId: Inconnu }), annuaire), 'Fiche 99999999')
  })

  test('un compte visé est nommé par le même annuaire', () => {
    assert.equal(cibleDe(acces({ ressource: 'Compte', nature: 'Desactivation', cibleId: Compte1 }), annuaire), 'Claire Martin')
  })

  // ⚠️ L'identifiant de l'absence est posé dans l'annuaire des agents : une résolution
  // naïve « par n'importe quelle liste » la nommerait. C'est ce qui ne doit pas arriver.
  test('une absence n’est jamais nommée, même si son identifiant résout ailleurs', () => {
    const piege: Annuaire = { ...annuaire, agents: new Map([[Inconnu, 'Paul Durand']]) }

    assert.equal(cibleDe(acces({ ressource: 'Absence', cibleId: Inconnu }), piege), 'Absence 99999999')
  })

  test('une liste dit son nombre de lignes, au pluriel juste', () => {
    assert.equal(cibleDe(acces({ nature: 'Liste', cibleId: null, lignes: 12 }), annuaire), '12 lignes')
    assert.equal(cibleDe(acces({ nature: 'Liste', cibleId: null, lignes: 1 }), annuaire), '1 ligne')
    assert.equal(cibleDe(acces({ nature: 'Liste', cibleId: null, lignes: 0 }), annuaire), '0 ligne')
  })

  test('une liste de candidats dit aussi pour quelle vacation, sans la nommer', () => {
    assert.equal(
      cibleDe(acces({ ressource: 'Candidats', nature: 'Liste', cibleId: Inconnu, lignes: 4 }), annuaire),
      '4 lignes, pour 99999999',
    )
  })

  test('une lecture sans cible rend un tiret', () => {
    assert.equal(cibleDe(acces({ cibleId: null }), annuaire), '—')
  })
})

describe('la période', () => {
  // Lu en heure locale : quel que soit le fuseau, l'instant envoyé est le minuit local du
  // premier jour, et celui du lendemain du dernier.
  function local(instant: string): string {
    const d = new Date(instant)
    return `${String(d.getFullYear())}-${String(d.getMonth() + 1)}-${String(d.getDate())} ${String(d.getHours())}:${String(d.getMinutes())}`
  }

  test('du début du premier jour au début du lendemain du dernier, en heure locale', () => {
    const { depuis, jusqua } = periodeDe('2026-11-12', '2026-11-12')

    assert.equal(local(depuis ?? ''), '2026-11-12 0:0')
    assert.equal(local(jusqua ?? ''), '2026-11-13 0:0')
  })

  test('à Paris, le 12 novembre commence la veille à 23 h UTC', { skip: process.env.TZ !== 'Europe/Paris' }, () => {
    assert.deepEqual(periodeDe('2026-11-12', '2026-11-12'), {
      depuis: '2026-11-11T23:00:00.000Z',
      jusqua: '2026-11-12T23:00:00.000Z',
    })
  })

  // Le dernier dimanche de mars, Paris passe à l'heure d'été : le jour dure 23 heures, et
  // ajouter 24 heures au minuit tomberait à une heure du matin le lendemain.
  test('un jour de changement d’heure se borne au minuit local du lendemain', () => {
    const { jusqua } = periodeDe('2026-03-29', '2026-03-29')

    assert.equal(local(jusqua ?? ''), '2026-3-30 0:0')
  })

  test('une borne vide n’envoie rien', () => {
    assert.deepEqual(periodeDe('', ''), {})
    assert.deepEqual(Object.keys(periodeDe('2026-11-12', '')), ['depuis'])
    assert.deepEqual(Object.keys(periodeDe('', '2026-11-12')), ['jusqua'])
  })

  test('une fin avant le début est refusée avant d’appeler ; un seul jour ne l’est pas', () => {
    assert.equal(refusDePeriode('2026-11-12', '2026-11-11'), 'La fin de la période précède son début.')
    assert.equal(refusDePeriode('2026-11-12', '2026-11-12'), null)
    assert.equal(refusDePeriode('', '2026-11-11'), null)
  })
})

describe('la requête et l’heure affichée', () => {
  test('sans filtre, seules la page et la taille partent', () => {
    assert.equal(routeDuJournal(FiltresVides, 2, 50), '/api/journal-acces?page=2&taille=50')
  })

  test('chaque filtre posé part, et les instants sont encodés', () => {
    const route = routeDuJournal(
      { utilisateurId: Compte1, ressource: 'Agent', nature: 'Export', du: '2026-11-12', au: '2026-11-12' },
      1,
      50,
    )
    const parametres = new URL(route, 'https://klean.test').searchParams

    assert.equal(parametres.get('utilisateurId'), Compte1)
    assert.equal(parametres.get('ressource'), 'Agent')
    assert.equal(parametres.get('nature'), 'Export')
    assert.equal(parametres.get('depuis'), periodeDe('2026-11-12', '').depuis)
    assert.equal(parametres.get('jusqua'), periodeDe('', '2026-11-12').jusqua)
    assert.ok(!route.includes(':'), 'les « : » des instants sont encodés')
  })

  test('l’heure d’un accès se lit en heure locale', () => {
    assert.match(formaterInstant('2026-11-12T09:05:00Z'), /^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2}$/)
  })

  test('à Paris, 9 h 05 UTC en novembre se lit 10:05', { skip: process.env.TZ !== 'Europe/Paris' }, () => {
    assert.equal(formaterInstant('2026-11-12T09:05:00Z'), '12/11/2026 10:05')
  })
})
