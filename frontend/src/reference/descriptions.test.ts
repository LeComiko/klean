import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import { Roles } from '../types/auth.ts'
import { Descriptions, descriptionDe, descriptionsLisibles } from './descriptions.ts'

/**
 * `descriptions.ts` est la source unique des écrans de référence : un seul motif
 * les rend tous, donc une faute dans une description est une faute dans un écran
 * entier, et elle ne se voit pas en relisant du code de composant.
 *
 * Deux familles de vérifications, et la distinction compte.
 *
 * **Les invariantes structurelles** — une colonne qui désigne un champ inexistant,
 * un `choix` sans valeurs, une référence vers une ressource absente — sont
 * vérifiables ici, et chacune produirait une colonne vide ou une liste déroulante
 * morte à l'écran, sans aucune erreur.
 *
 * **Les droits**, en revanche, ne peuvent pas être vérifiés contre la matrice de
 * `SPEC.md` : la recopier ici ne prouverait que la recopie. Ce qui est éprouvé est
 * ce qui reste vrai quelle que soit la matrice : on n'écrit pas ce qu'on ne lit
 * pas, l'administrateur écrit toute donnée de référence, et aucun rôle sans écran
 * n'y figure. L'autorité reste le serveur, qui répond 403.
 */
describe('descriptions — structure', () => {
  test('les clés, chemins et routes sont uniques', () => {
    for (const champ of ['cle', 'chemin', 'route'] as const) {
      const valeurs = Descriptions.map((description) => description[champ])

      assert.equal(new Set(valeurs).size, valeurs.length, `${champ} : ${valeurs.join(' ')}`)
    }
  })

  test('chaque route d’API commence par /api/ et sans identifiant', () => {
    for (const description of Descriptions) {
      assert.ok(description.route.startsWith('/api/'), description.route)
      // `listerRessource` accole `?page=…` : une route qui porterait déjà une
      // chaîne de requête produirait `?page=1?taille=25`.
      assert.equal(description.route.includes('?'), false, description.route)
      assert.equal(description.route.endsWith('/'), false, description.route)
    }
  })

  test('chaque description nomme ses libellés', () => {
    for (const description of Descriptions) {
      assert.notEqual(description.libelle, '', description.cle)
      assert.notEqual(description.singulier, '', description.cle)
      assert.notEqual(description.sousTitre, '', description.cle)
      // Le singulier compose « Nouveau client » et « Aucun client » : une
      // majuscule ou un article ici donnerait « Nouveau Le client ».
      assert.equal(description.singulier, description.singulier.toLowerCase(), description.cle)
    }
  })

  test('les noms de champs sont uniques dans une description', () => {
    for (const description of Descriptions) {
      const noms = description.champs.map((champ) => champ.nom)

      assert.equal(new Set(noms).size, noms.length, `${description.cle} : ${noms.join(' ')}`)
    }
  })

  test('chaque colonne désigne un champ ou l’identifiant', () => {
    // Le défaut que ce test attrape : une colonne `ville` sur une ressource qui
    // porte `adresse`. À l'écran, une colonne intitulée « Adresse » et vide sur
    // toutes les lignes — ce qui ressemble à des données manquantes.
    for (const description of Descriptions) {
      const connus = new Set([...description.champs.map((champ) => champ.nom), 'id'])

      for (const colonne of description.colonnes) {
        assert.ok(connus.has(colonne.nom), `${description.cle} → colonne ${colonne.nom}`)
      }
    }
  })

  test('l’étiquette désigne un champ existant', () => {
    // Elle nomme la ligne dans « Supprimer « … » ? ». Un nom de champ faux y
    // afficherait le singulier de la ressource, donc « Supprimer « client » ? » sur
    // toutes les lignes — une confirmation qui ne confirme rien.
    for (const description of Descriptions) {
      const noms = new Set(description.champs.map((champ) => champ.nom))

      assert.ok(noms.has(description.etiquette), `${description.cle} → ${description.etiquette}`)
    }
  })

  test('un champ de choix propose des valeurs, un autre type n’en propose pas', () => {
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (champ.type === 'choix') {
          assert.ok((champ.choix ?? []).length > 0, `${description.cle} → ${champ.nom}`)
        } else {
          assert.equal(champ.choix, undefined, `${description.cle} → ${champ.nom}`)
        }
      }
    }
  })

  test('un champ de référence vise une description existante', () => {
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (champ.type !== 'reference') {
          assert.equal(champ.reference, undefined, `${description.cle} → ${champ.nom}`)
          continue
        }

        assert.notEqual(champ.reference, undefined, `${description.cle} → ${champ.nom}`)
        assert.notEqual(
          descriptionDe(champ.reference ?? ''),
          undefined,
          `${description.cle} → ${champ.nom} → ${String(champ.reference)}`,
        )
      }
    }
  })

  test('une colonne de référence vise aussi une description existante', () => {
    for (const description of Descriptions) {
      for (const colonne of description.colonnes) {
        if (colonne.reference === undefined) continue

        assert.notEqual(
          descriptionDe(colonne.reference),
          undefined,
          `${description.cle} → ${colonne.nom}`,
        )
      }
    }
  })

  test('une parenté désigne une description et un de ses propres champs', () => {
    for (const description of Descriptions) {
      const parent = description.parent

      if (parent === undefined) continue

      assert.notEqual(descriptionDe(parent.ressource), undefined, description.cle)

      const champ = description.champs.find((candidat) => candidat.nom === parent.champ)

      assert.notEqual(champ, undefined, `${description.cle} → ${parent.champ}`)
      // La cohérence qui compte : le champ porteur de la clé étrangère doit viser
      // la même ressource que la parenté, sinon la liste déroulante propose des
      // agents là où le serveur attend un client.
      assert.equal(champ?.reference, parent.ressource, `${description.cle} → ${parent.champ}`)
    }
  })

  test('une parenté déclarée a un parent qui précède l’enfant', () => {
    // L'ordre de la chaîne : on ne peut pas créer un site avant son client. La
    // sous-navigation affiche cet ordre, et c'est ce qui rend la mise en route
    // d'une entreprise possible de haut en bas.
    const rang = new Map(Descriptions.map((description, index) => [description.cle, index]))

    for (const description of Descriptions) {
      if (description.parent === undefined) continue

      const parent = rang.get(description.parent.ressource)
      const enfant = rang.get(description.cle)

      assert.ok(
        parent !== undefined && enfant !== undefined && parent < enfant,
        `${description.cle} précède son parent ${description.parent.ressource}`,
      )
    }
  })

  test('les bornes d’un champ nombre sont cohérentes', () => {
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (champ.type !== 'nombre') continue

        if (champ.min !== undefined && champ.max !== undefined) {
          assert.ok(champ.min < champ.max, `${description.cle} → ${champ.nom}`)
        }
      }
    }
  })

  test('tout champ de texte court déclare sa longueur maximale', () => {
    // La garde qui ferme le chemin du message anglais. Les DTOs portent un
    // `[MaxLength]` sur chacun de ces champs, et la validation de modèle d'ASP.NET
    // s'exécute avant le contrôleur : son refus traverse l'API en anglais, et rien
    // côté écran ne peut le traduire (CLAUDE.md §6). Un `maxLength` côté navigateur
    // rend ce refus inatteignable. Ajouter un champ sans sa longueur rouvrirait le
    // chemin sans qu'aucun écran ne change d'apparence.
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (champ.type !== 'texte' && champ.type !== 'courriel' && champ.type !== 'telephone') {
          continue
        }

        assert.ok(
          champ.longueurMax !== undefined && champ.longueurMax > 0,
          `${description.cle} → ${champ.nom}`,
        )
      }
    }
  })

  test('une longueur maximale ne porte que sur du texte', () => {
    // `maxLength` n'existe ni sur un `<select>` ni sur une case à cocher, et sur un
    // `<input type="number">` il compte des caractères, pas une valeur — les bornes
    // d'un nombre passent par min et max.
    const textuels = new Set(['texte', 'texteLong', 'courriel', 'telephone'])

    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (champ.longueurMax === undefined) continue

        assert.ok(textuels.has(champ.type), `${description.cle} → ${champ.nom} (${champ.type})`)
      }
    }
  })

  test('seul un booléen porte un défaut booléen', () => {
    // Un `defaut: true` sur un champ texte partirait en `true` dans le corps JSON
    // et se ferait refuser par la liaison de modèle.
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (typeof champ.defaut !== 'boolean') continue

        assert.equal(champ.type, 'booleen', `${description.cle} → ${champ.nom}`)
      }
    }
  })
})

describe('descriptions — droits', () => {
  test('on n’écrit jamais ce qu’on ne lit pas', () => {
    for (const description of Descriptions) {
      for (const role of description.rolesEcriture) {
        assert.ok(
          description.rolesLecture.includes(role),
          `${description.cle} → ${role} écrit sans lire`,
        )
      }
    }
  })

  test('l’administrateur écrit toutes les données de référence', () => {
    for (const description of Descriptions) {
      assert.ok(
        description.rolesEcriture.includes(Roles.Administrateur),
        description.cle,
      )
    }
  })

  test('aucune description n’ouvre quoi que ce soit au rôle Agent', () => {
    // La matrice ne lui donne que ses propres interventions. Un écran de référence
    // qui s'ouvrirait à lui exposerait la liste des agents de l'entreprise, et
    // l'adresse de leur domicile — une donnée personnelle, objet de l'étape 17.
    for (const description of Descriptions) {
      assert.equal(description.rolesLecture.includes(Roles.Agent), false, description.cle)
      assert.equal(description.rolesEcriture.includes(Roles.Agent), false, description.cle)
    }
  })

  test('le ClientSyndic ne lit que les clients et les sites, et n’écrit rien', () => {
    const lisibles = Descriptions.filter((description) =>
      description.rolesLecture.includes(Roles.ClientSyndic),
    )

    assert.deepEqual(lisibles.map((description) => description.cle), ['clients', 'sites'])

    for (const description of Descriptions) {
      assert.equal(description.rolesEcriture.includes(Roles.ClientSyndic), false, description.cle)
    }
  })

  test('le Responsable lit les données de référence sans y écrire', () => {
    // C'est la décision de l'étape : son formulaire est présenté en lecture seule,
    // sans bouton d'enregistrement. Le jour où la matrice s'ouvre — déclencheur
    // écrit à l'étape 8, « combien de personnes connaissent le mot de passe
    // administrateur ? » — ce test devra changer, et c'est ce qu'on veut qu'il
    // signale.
    for (const description of Descriptions) {
      assert.ok(description.rolesLecture.includes(Roles.Responsable), description.cle)
      assert.equal(description.rolesEcriture.includes(Roles.Responsable), false, description.cle)
    }
  })
})

describe('descriptionsLisibles', () => {
  test('rend les ressources ouvertes, dans l’ordre de la chaîne', () => {
    const cles = ['clients', 'sites']

    assert.deepEqual(
      descriptionsLisibles(cles, [Roles.Administrateur]).map((description) => description.cle),
      ['clients', 'sites'],
    )
  })

  test('écarte celles que le rôle ne lit pas', () => {
    assert.deepEqual(
      descriptionsLisibles(['clients', 'agents'], [Roles.ClientSyndic]).map((d) => d.cle),
      ['clients'],
    )
  })

  test('rend une liste vide plutôt que d’échouer sur une clé inconnue', () => {
    // La sous-navigation reçoit ce résultat ; une exception y remplacerait l'écran
    // entier par une page blanche, pour une clé mal orthographiée.
    assert.deepEqual(descriptionsLisibles(['inexistante'], [Roles.Administrateur]), [])
  })

  test('un rôle sans droit ne lit rien', () => {
    assert.deepEqual(descriptionsLisibles(['clients', 'sites', 'agents'], [Roles.Agent]), [])
    assert.deepEqual(descriptionsLisibles(['clients'], []), [])
  })
})
