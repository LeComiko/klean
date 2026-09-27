import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import { Roles } from '../types/auth.ts'
import { Sections, premiereSectionOuverte } from './sections.ts'

/**
 * `Sections` est la source unique de la navigation, des routes et de la
 * destination d'arrivée. Ces trois usages doivent rester cohérents avec la matrice
 * des droits de `SPEC.md` étape 8 : une pastille visible dont la route n'est pas
 * gardée, ou une arrivée sur une section fermée au rôle, sont les deux formes
 * précises que prend la divergence.
 */
describe('sections — navigation et destination d’arrivée', () => {
  test('aucune section ne mène au tableau de bord retiré', () => {
    // La pastille TB accueillait tout le monde mais menait à « en chantier », et
    // aucune étape ne prévoyait de l'écrire. Ce test garde son absence.
    assert.equal(Sections.some((section) => section.chemin === '/tableau-de-bord'), false)
    assert.equal(Sections.some((section) => section.pastille === 'TB'), false)
  })

  test('les pastilles sont celles de SPEC.md étape 13, moins TB', () => {
    assert.deepEqual(Sections.map((section) => section.pastille), ['PL', 'SI', 'AG', 'ST'])
  })

  test('chaque section déclare un chemin absolu et un libellé', () => {
    for (const section of Sections) {
      assert.ok(section.chemin.startsWith('/'), section.chemin)
      assert.notEqual(section.libelle, '')
      assert.equal(section.pastille.length, 2, section.pastille)
    }
  })

  describe('destination d’arrivée par rôle', () => {
    test('les trois rôles internes arrivent sur le planning', () => {
      for (const role of [Roles.Administrateur, Roles.Responsable, Roles.ChefEquipe]) {
        assert.equal(premiereSectionOuverte([role])?.chemin, '/planning', role)
      }
    })

    test('un ClientSyndic arrive sur Sites, la seule section qui lui est ouverte', () => {
      // La matrice lui donne la lecture sur Clients et Sites, cadrée sur son propre
      // ClientId. Le planning nomme tous les agents de l'entreprise : il lui est
      // fermé.
      const ouvertes = Sections.filter(
        (section) => section.roles === undefined || section.roles.includes(Roles.ClientSyndic),
      )

      assert.deepEqual(ouvertes.map((section) => section.chemin), ['/sites'])
      assert.equal(premiereSectionOuverte([Roles.ClientSyndic])?.chemin, '/sites')
    })

    test('un Agent n’a aucune section, et c’est l’état documenté du produit', () => {
      // SPEC.md étape 8 : « Cette étape sécurise le rôle, elle ne le sert pas. »
      // Son parcours suppose une application mobile et des endpoints conçus pour
      // elle. Le jour où un écran lui est destiné, ce test devra changer — et c'est
      // exactement ce qu'on veut qu'il signale.
      assert.equal(premiereSectionOuverte([Roles.Agent]), undefined)
    })

    test('un compte sans rôle n’ouvre rien', () => {
      assert.equal(premiereSectionOuverte([]), undefined)
    })

    test('plusieurs rôles ouvrent la section la plus haute', () => {
      assert.equal(
        premiereSectionOuverte([Roles.ClientSyndic, Roles.ChefEquipe])?.chemin,
        '/planning',
      )
    })
  })
})
