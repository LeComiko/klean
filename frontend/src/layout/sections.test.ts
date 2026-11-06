import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import { descriptionDe } from '../reference/descriptions.ts'
import { Roles } from '../types/auth.ts'
import { CheminCompte, Sections, ongletsDe, premiereSectionOuverte } from './sections.ts'

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

  test('les pastilles sont celles de SPEC.md étape 13, moins TB, revues aux étapes 22 et 25', () => {
    // `SI` est devenu `CL` : la pastille dit la ressource où l'on arrive. `AN` et `MJ`
    // sont les deux écrans du passage de l'agent.
    assert.deepEqual(
      Sections.map((section) => section.pastille),
      ['PL', 'AN', 'CL', 'AG', 'ST', 'EN', 'MJ'],
    )
  })

  test('chaque section déclare un chemin absolu et un libellé', () => {
    for (const section of Sections) {
      assert.ok(section.chemin.startsWith('/'), section.chemin)
      assert.notEqual(section.libelle, '')
      assert.equal(section.pastille.length, 2, section.pastille)
    }
  })

  describe('les sections qui portent une chaîne de ressources', () => {
    const avecRessources = Sections.filter((section) => section.ressources !== undefined)

    test('chaque clé de ressource existe dans descriptions.ts', () => {
      for (const section of avecRessources) {
        for (const cle of section.ressources ?? []) {
          assert.notEqual(descriptionDe(cle), undefined, `${section.pastille} → ${cle}`)
        }
      }
    })

    test('le chemin d’une section est celui de sa première ressource', () => {
      // C'est ce qui rend la destination d'arrivée atteignable : `App.tsx` ne monte
      // aucune route pour le chemin de la section elle-même, seulement pour celui de
      // chaque ressource. Un chemin de section qui ne correspond à aucune ressource
      // donnerait une redirection vers une route inexistante — donc, par la route
      // attrape-tout, une boucle vers l'accueil.
      for (const section of avecRessources) {
        const premiere = descriptionDe((section.ressources ?? [])[0] ?? '')

        assert.equal(section.chemin, premiere?.chemin, section.pastille)
      }
    })

    test('les rôles d’une section valent l’union des lectures de ses ressources et écrans', () => {
      // L'invariante qui empêche les deux divergences symétriques : une pastille
      // visible dont toutes les listes répondent 403, et une liste lisible dont la
      // pastille est cachée.
      for (const section of avecRessources) {
        const union = new Set<string>()

        for (const cle of section.ressources ?? []) {
          for (const role of descriptionDe(cle)?.rolesLecture ?? []) union.add(role)
        }

        for (const ecran of section.ecrans ?? []) {
          for (const role of ecran.roles) union.add(role)
        }

        assert.deepEqual(
          [...(section.roles ?? [])].sort(),
          [...union].sort(),
          section.pastille,
        )
      }
    })

    test('aucun chemin de ressource ou d’écran n’est monté deux fois', () => {
      const chemins = [
        ...Sections.map((section) => section.chemin).filter((chemin) =>
          !avecRessources.some((section) => section.chemin === chemin)),
        ...avecRessources.flatMap((section) => [
          ...(section.ressources ?? []).map((cle) => descriptionDe(cle)?.chemin ?? cle),
          ...(section.ecrans ?? []).map((ecran) => ecran.chemin),
        ]),
        CheminCompte,
      ]

      assert.equal(new Set(chemins).size, chemins.length, chemins.join(' '))
    })

    test('aucune section sans ressources ne collisionne avec une route de ressource', () => {
      const deRessources = new Set(
        avecRessources.flatMap((section) =>
          (section.ressources ?? []).map((cle) => descriptionDe(cle)?.chemin),
        ),
      )

      for (const section of Sections.filter((candidate) => candidate.ressources === undefined)) {
        assert.equal(deRessources.has(section.chemin), false, section.chemin)
      }
    })
  })

  describe('les onglets du stock', () => {
    const stock = Sections.find((section) => section.pastille === 'ST')

    test('les ressources dans l’ordre de la mise en route, puis les écrans', () => {
      assert.ok(stock)
      assert.deepEqual(
        ongletsDe(stock, [Roles.Administrateur]).map((onglet) => onglet.chemin),
        ['/produits', '/emplacements', '/consommations', '/stock', '/prevision'],
      )
    })

    test('le chef d’équipe n’a pas l’onglet de la prévision, qui lui répondrait 403', () => {
      // Ses vacations sont filtrées sur son équipe, le stock non : sa prévision
      // comparerait le besoin d'une équipe au stock de l'entreprise.
      assert.ok(stock)
      const chemins = ongletsDe(stock, [Roles.ChefEquipe]).map((onglet) => onglet.chemin)

      assert.ok(chemins.includes('/stock'))
      assert.equal(chemins.includes('/prevision'), false)
    })
  })

  describe('destination d’arrivée par rôle', () => {
    test('les trois rôles internes arrivent sur le planning', () => {
      for (const role of [Roles.Administrateur, Roles.Responsable, Roles.ChefEquipe]) {
        assert.equal(premiereSectionOuverte([role])?.chemin, '/planning', role)
      }
    })

    test('un ClientSyndic arrive sur Clients, la seule section qui lui est ouverte', () => {
      // La matrice lui donne la lecture sur Clients et Sites, cadrée sur son propre
      // ClientId. Le planning nomme tous les agents de l'entreprise : il lui est
      // fermé.
      const ouvertes = Sections.filter(
        (section) => section.roles === undefined || section.roles.includes(Roles.ClientSyndic),
      )

      assert.deepEqual(ouvertes.map((section) => section.chemin), ['/clients'])
      assert.equal(premiereSectionOuverte([Roles.ClientSyndic])?.chemin, '/clients')
    })

    test('un Agent arrive sur sa journée, la seule section qui lui est ouverte', () => {
      // Jusqu'à l'étape 25, ce test gardait l'inverse : « sécurisé, pas servi » (SPEC.md
      // étape 8). Il a changé avec le premier écran conçu pour le rôle, comme prévu.
      const ouvertes = Sections.filter((section) => section.roles?.includes(Roles.Agent) === true)

      assert.deepEqual(ouvertes.map((section) => section.chemin), ['/ma-journee'])
      assert.equal(premiereSectionOuverte([Roles.Agent])?.chemin, '/ma-journee')
    })

    test('aucun rôle interne ni le syndic n’ont la journée d’un agent', () => {
      // Ses routes sont réservées au rôle Agent : la pastille leur répondrait 403.
      for (const role of [Roles.Administrateur, Roles.Responsable, Roles.ChefEquipe, Roles.ClientSyndic]) {
        const journee = Sections.find((section) => section.chemin === '/ma-journee')

        assert.equal(journee?.roles?.includes(role), false, role)
      }
    })

    test('un compte agent et interne arrive sur le planning, pas sur sa journée', () => {
      assert.equal(premiereSectionOuverte([Roles.Agent, Roles.Responsable])?.chemin, '/planning')
    })

    test('les anomalies sont aux trois rôles internes, jamais à l’agent ni au syndic', () => {
      // Rien pour le syndic avant le portail (SPEC.md, étape 25).
      const anomalies = Sections.find((section) => section.chemin === '/anomalies')

      assert.deepEqual([...(anomalies?.roles ?? [])].sort(), [Roles.Administrateur, Roles.ChefEquipe, Roles.Responsable].sort())
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

  describe('l’écran de compte, seul chemin qui n’est pas une section', () => {
    test('aucune section ne revendique le chemin du compte', () => {
      // Deux `<Route>` sur le même chemin laisseraient la seconde inatteignable,
      // sans erreur ni avertissement. `App.tsx` monte `/compte` à part parce qu'il
      // n'est ouvert par aucun rôle et ne doit jamais devenir une destination
      // d'arrivée ; ce test garde la séparation.
      assert.equal(Sections.some((section) => section.chemin === CheminCompte), false)
    })

    test('aucune ressource de référence ne revendique le chemin du compte', () => {
      for (const section of Sections) {
        for (const cle of section.ressources ?? []) {
          assert.notEqual(descriptionDe(cle)?.chemin, CheminCompte, cle)
        }
      }
    })

    test('le compte n’est la destination d’arrivée d’aucun rôle', () => {
      // Il est ouvert à tout compte connecté : s'il devenait une section, il
      // deviendrait l'arrivée de tous — y compris des rôles internes, à la place du
      // planning.
      for (const role of Object.values(Roles)) {
        assert.notEqual(premiereSectionOuverte([role])?.chemin, CheminCompte, role)
      }
    })
  })
})
