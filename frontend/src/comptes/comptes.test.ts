import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

// ⚠️ Extension explicite : `node --test` n'applique pas la résolution sans
// extension de Vite (CLAUDE.md §10).
import {
  corpsDeCreation,
  etatDe,
  filtrerParRole,
  gestesDe,
  libelleRole,
  nomDe,
  questionDe,
  SaisieVide,
} from './comptes.ts'
import type { Compte } from '../types/comptes.ts'

const Moi = 'moi'

function compte(modifs: Partial<Compte> = {}): Compte {
  return {
    id: 'c1',
    email: 'claire@exemple.fr',
    nom: 'Martin',
    prenom: 'Claire',
    role: 'Responsable',
    actif: true,
    secondFacteurActif: false,
    motDePasseProvisoire: false,
    agentId: null,
    clientId: null,
    dateCreation: '2026-11-06T10:00:00Z',
    dateAnonymisation: null,
    ...modifs,
  }
}

const saisie = { ...SaisieVide, email: 'claire@exemple.fr', nom: 'Martin', prenom: 'Claire', role: 'Responsable' }

describe('etatDe — un état par ligne', () => {
  test('la pseudonymisation l’emporte sur la fermeture', () => {
    assert.equal(etatDe(compte({ actif: false, dateAnonymisation: '2026-11-06T10:00:00Z' })), 'Pseudonymise')
  })

  test('la fermeture l’emporte sur la première connexion en attente', () => {
    assert.equal(etatDe(compte({ actif: false, motDePasseProvisoire: true })), 'Ferme')
  })

  test('un compte jamais connecté le dit', () => {
    assert.equal(etatDe(compte({ motDePasseProvisoire: true })), 'PremiereConnexion')
  })

  test('sinon, actif', () => {
    assert.equal(etatDe(compte()), 'Actif')
  })
})

describe('gestesDe — ce que propose une ligne', () => {
  test('rien sur son propre compte : le serveur refuserait chaque geste', () => {
    assert.deepEqual(gestesDe(compte({ id: Moi, secondFacteurActif: true }), Moi), [])
  })

  test('rien sur un compte pseudonymisé', () => {
    assert.deepEqual(gestesDe(compte({ actif: false, dateAnonymisation: '2026-11-06T10:00:00Z' }), Moi), [])
  })

  test('un compte actif se ferme, et se pseudonymise sans fiche agent', () => {
    assert.deepEqual(gestesDe(compte(), Moi), ['Fermer', 'Pseudonymiser'])
  })

  test('un compte fermé se rouvre', () => {
    assert.deepEqual(gestesDe(compte({ actif: false }), Moi), ['Rouvrir', 'Pseudonymiser'])
  })

  test('la réinitialisation n’est proposée qu’à un second facteur actif', () => {
    assert.deepEqual(gestesDe(compte({ secondFacteurActif: true }), Moi), ['Fermer', 'Reinitialiser', 'Pseudonymiser'])
  })

  test('un compte rattaché à une fiche agent ne se pseudonymise pas ici', () => {
    assert.deepEqual(gestesDe(compte({ agentId: 'a1' }), Moi), ['Fermer'])
  })
})

describe('questionDe — la confirmation nomme et dit l’effet', () => {
  test('les trois gestes qui coupent quelque chose demandent confirmation', () => {
    for (const geste of ['Fermer', 'Reinitialiser', 'Pseudonymiser'] as const) {
      assert.match(questionDe(geste, 'Claire Martin') ?? '', /Claire Martin/)
    }
  })

  test('la pseudonymisation dit qu’elle est irréversible', () => {
    assert.match(questionDe('Pseudonymiser', 'Claire Martin') ?? '', /Irréversible/)
  })

  test('rouvrir part sans confirmation', () => {
    assert.equal(questionDe('Rouvrir', 'Claire Martin'), null)
  })
})

describe('nomDe et libelleRole', () => {
  test('prénom puis nom', () => {
    assert.equal(nomDe(compte()), 'Claire Martin')
  })

  test('un prénom vide ne laisse pas d’espace devant', () => {
    assert.equal(nomDe(compte({ prenom: '' })), 'Martin')
  })

  test('sans nom ni prénom, l’adresse', () => {
    assert.equal(nomDe(compte({ prenom: '', nom: '' })), 'claire@exemple.fr')
  })

  test('les rôles parlent français, un rôle inconnu reste lisible', () => {
    assert.equal(libelleRole('ChefEquipe'), 'Chef d’équipe')
    assert.equal(libelleRole('Comptable'), 'Comptable')
    assert.equal(libelleRole(''), 'Aucun rôle')
  })
})

describe('corpsDeCreation — ce qui part, ou ce qui est refusé', () => {
  test('une saisie complète part, rognée', () => {
    assert.deepEqual(corpsDeCreation({ ...saisie, email: ' claire@exemple.fr ', nom: ' Martin ' }), {
      corps: { email: 'claire@exemple.fr', nom: 'Martin', prenom: 'Claire', role: 'Responsable', agentId: null, clientId: null },
    })
  })

  test('l’adresse vide, mal formée, et le nom vide sont refusés en français', () => {
    assert.match(refusDe({ ...saisie, email: '' }), /obligatoire/)
    assert.match(refusDe({ ...saisie, email: 'claire.exemple.fr' }), /pas valide/)
    assert.match(refusDe({ ...saisie, email: 'claire@exemple@fr' }), /pas valide/)
    assert.match(refusDe({ ...saisie, nom: '  ' }), /nom est obligatoire/)
  })

  test('un rôle est obligatoire, et doit en être un', () => {
    assert.match(refusDe({ ...saisie, role: '' }), /rôle/)
    assert.match(refusDe({ ...saisie, role: 'Comptable' }), /rôle/)
  })

  test('un compte Agent exige sa fiche', () => {
    assert.match(refusDe({ ...saisie, role: 'Agent' }), /fiche/)
    assert.equal(corpsDe({ ...saisie, role: 'Agent', agentId: 'a1' }).agentId, 'a1')
  })

  test('un client syndic exige son client', () => {
    assert.match(refusDe({ ...saisie, role: 'ClientSyndic' }), /client/)
    assert.equal(corpsDe({ ...saisie, role: 'ClientSyndic', clientId: 'k1' }).clientId, 'k1')
  })

  test('une fiche agent reste facultative pour un rôle interne', () => {
    assert.equal(corpsDe(saisie).agentId, null)
    assert.equal(corpsDe({ ...saisie, role: 'Administrateur', agentId: 'a1' }).agentId, 'a1')
  })

  test('un champ masqué part à null : la fiche choisie avant un changement de rôle ne suit pas', () => {
    const corps = corpsDe({ ...saisie, role: 'ClientSyndic', agentId: 'a1', clientId: 'k1' })
    assert.equal(corps.agentId, null)
    assert.equal(corpsDe({ ...saisie, clientId: 'k1' }).clientId, null)
  })
})

describe('filtrerParRole', () => {
  const comptes = [compte({ id: 'a', role: 'Agent' }), compte({ id: 'r' }), compte({ id: 'b', role: 'Agent' })]

  test('un filtre vide rend tout', () => {
    assert.equal(filtrerParRole(comptes, '').length, 3)
  })

  test('un rôle rend les siens, dans l’ordre du serveur', () => {
    assert.deepEqual(filtrerParRole(comptes, 'Agent').map((c) => c.id), ['a', 'b'])
  })
})

function refusDe(valeur: typeof saisie): string {
  const resultat = corpsDeCreation(valeur)
  return 'refus' in resultat ? resultat.refus : ''
}

function corpsDe(valeur: typeof saisie) {
  const resultat = corpsDeCreation(valeur)
  assert.ok('corps' in resultat, 'refusé à tort')
  return resultat.corps
}
