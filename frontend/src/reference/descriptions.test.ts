import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import { Roles } from '../types/auth.ts'
import { Descriptions, descriptionDe, descriptionsLisibles, enfantsDe } from './descriptions.ts'

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

  test('l’étiquette n’est pas un champ de référence', () => {
    // ⚠️ L'invariante la moins évidente du fichier, et elle a une conséquence
    // visible. L'étiquette sert deux fois : nommer une ligne, et **libeller les
    // options de la liste déroulante d'une ressource enfant**. Sur l'écran des
    // prestations, le choix des contrats est alimenté par `/api/contrats` seul — les
    // sites ne sont pas chargés, et rien ne peut résoudre un `siteId` en nom. Une
    // étiquette de référence afficherait donc une liste d'identifiants, qu'aucune
    // erreur ne signalerait.
    for (const description of Descriptions) {
      const champ = description.champs.find((candidat) => candidat.nom === description.etiquette)

      assert.notEqual(champ?.type, 'reference', `${description.cle} → ${description.etiquette}`)
      assert.notEqual(champ?.type, 'booleen', `${description.cle} → ${description.etiquette}`)
    }
  })

  test('un champ masqué n’est jamais obligatoire', () => {
    // Les deux ensemble sont contradictoires : `corpsDe` envoie `null` sur un champ
    // masqué, et le serveur refuserait un obligatoire nul. L'écran n'afficherait même
    // pas le champ dont le refus parle.
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        if (champ.masquerSi === undefined) continue

        assert.notEqual(champ.requis, true, `${description.cle} → ${champ.nom}`)
      }
    }
  })

  test('une condition de masquage désigne un choix de la même description', () => {
    // Une faute de frappe sur le champ pivot, ou sur une valeur de l'énumération,
    // rendrait la condition éternellement fausse : le champ resterait affiché, et
    // c'est précisément ce que le masquage existe pour empêcher.
    for (const description of Descriptions) {
      for (const champ of description.champs) {
        const condition = champ.masquerSi

        if (condition === undefined) continue

        const pivot = description.champs.find((candidat) => candidat.nom === condition.champ)

        assert.notEqual(pivot, undefined, `${description.cle} → ${condition.champ}`)
        assert.equal(pivot?.type, 'choix', `${description.cle} → ${condition.champ}`)
        assert.ok(condition.vaut.length > 0, `${description.cle} → ${champ.nom}`)

        for (const valeur of condition.vaut) {
          assert.ok(
            (pivot?.choix ?? []).includes(valeur),
            `${description.cle} → ${champ.nom} masqué sur « ${valeur} », absent du choix`,
          )
        }
      }
    }
  })

  test('le motif d’une absence disparaît sur un arrêt maladie', () => {
    // ⚠️ Nommément, et pas seulement par l'invariante générale ci-dessus : c'est une
    // décision de conformité, pas un détail d'interface. Un motif d'arrêt maladie en
    // texte libre est une donnée de santé au sens de l'article 9 du RGPD, le planning
    // n'en fait rien, et `AbsencesController` le refuse déjà en 400. Ce test garde la
    // moitié « l'interface empêche » de la promesse faite à l'étape 8.
    //
    // L'étape 17 ajoutera `AccidentTravail` ici, en même temps que dans le serveur.
    const absences = descriptionDe('absences')
    const motif = absences?.champs.find((champ) => champ.nom === 'motif')

    assert.notEqual(motif, undefined)
    assert.deepEqual(motif?.masquerSi, { champ: 'type', vaut: ['Maladie'] })
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

  test('le Responsable lit tout, et n’écrit que les données d’exploitation', () => {
    // ⚠️ Ce test a changé au lot B, et la raison compte : la matrice des droits ne
    // s'est pas ouverte, c'est la liste des descriptions qui a dépassé les seules
    // données de **référence**. Une absence est une donnée d'exploitation — un
    // responsable la saisit, c'est son travail quotidien, et
    // `[Authorize(Roles = RolesKlean.Exploitation)]` sur `AbsencesController` l'y
    // autorise. Lui présenter un formulaire en lecture seule sur la seule chose que
    // l'étape 15 lui demande de saisir aurait été une faute d'interface exactement
    // symétrique de celle que tout ce motif évite.
    //
    // La liste est écrite ici parce qu'elle doit être **courte et délibérée** : y
    // ajouter une clé est une décision, pas un ajustement. L'autorité reste le
    // serveur, qui répond 403.
    const ecritesParLeResponsable = ['absences']

    for (const description of Descriptions) {
      assert.ok(description.rolesLecture.includes(Roles.Responsable), description.cle)

      assert.equal(
        description.rolesEcriture.includes(Roles.Responsable),
        ecritesParLeResponsable.includes(description.cle),
        description.cle,
      )
    }
  })

  test('le ChefEquipe lit sans écrire, sur toutes les ressources', () => {
    // Il reste le rôle de lecture pure de la matrice : aucun écran de cette étape ne
    // lui ouvre d'écriture, pas même sur les absences.
    for (const description of Descriptions) {
      assert.ok(description.rolesLecture.includes(Roles.ChefEquipe), description.cle)
      assert.equal(description.rolesEcriture.includes(Roles.ChefEquipe), false, description.cle)
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

describe('enfantsDe', () => {
  test('la chaîne du modèle se parcourt de haut en bas', () => {
    // La descente de niveau tout entière, dérivée des seules parentés déclarées.
    assert.deepEqual(enfantsDe('clients').map((d) => d.cle), ['sites'])
    assert.deepEqual(enfantsDe('sites').map((d) => d.cle), ['contrats'])
    assert.deepEqual(enfantsDe('contrats').map((d) => d.cle), ['prestations'])
    assert.deepEqual(enfantsDe('agents').map((d) => d.cle), ['absences'])
  })

  test('une feuille de la chaîne n’a pas d’enfant', () => {
    assert.deepEqual(enfantsDe('prestations'), [])
    assert.deepEqual(enfantsDe('absences'), [])
    // Les stocks auront leur étape : le jour où leur description existe, ce test
    // changera, et c'est ce qu'on veut qu'il signale.
    assert.deepEqual(enfantsDe('produits'), [])
  })

  test('une clé inconnue rend une liste vide plutôt que d’échouer', () => {
    assert.deepEqual(enfantsDe('inexistante'), [])
  })

  test('chaque enfant déclare le champ qui construit le lien', () => {
    // Le lien de descente vaut `${chemin}?${parent.champ}=${id}`. Un `parent.champ`
    // absent donnerait `?=…`, donc une liste non filtrée sous un bandeau qui annonce
    // un filtre — exactement ce que la série FiltresListeTests garde côté serveur.
    for (const description of Descriptions) {
      for (const enfant of enfantsDe(description.cle)) {
        assert.ok((enfant.parent?.champ ?? '') !== '', `${description.cle} → ${enfant.cle}`)
      }
    }
  })
})
