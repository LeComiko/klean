import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

// ⚠️ Extension explicite : `node --test` n'applique pas la résolution de Vite (CLAUDE.md §10).
import {
  AlphabetCode,
  DebutRefusCode,
  SaisieVide,
  corpsInstallation,
  normaliserCode,
  refusCode,
  refusCompte,
  refusDInstallation,
  refusEntreprise,
} from './installation.ts'
import type { Saisie } from './installation.ts'

const CodeBon = 'ABCDEFGHJKMNPQRSTUVW' // 20 symboles de l'alphabet

const complete: Saisie = {
  code: 'abcd-efgh jkmn pqrs-tuvw',
  raisonSociale: '  Propreté Durand  ',
  siret: '123 456 789 00012',
  territoire: 'Metropole',
  email: ' patron@exemple.fr ',
  emailConfirmation: 'patron@exemple.fr',
  nom: ' Durand ',
  prenom: ' Marie ',
  motDePasse: 'Secret2026Secret',
  confirmation: 'Secret2026Secret',
}

describe('normaliserCode — le même geste que CodeInstallation.Normaliser', () => {
  test('retire les espaces, dont tabulation, retour et insécable, et met en majuscules', () => {
    assert.equal(normaliserCode('abcd efgh\tjkmn\npqrs\u00A0tuvw'), 'ABCDEFGHJKMNPQRSTUVW')
  })

  // Un caractère par test : le sabotage d'un seul doit rougir un seul test.
  const retires: ReadonlyArray<readonly [string, string]> = [
    ['le tiret', '-'],
    ['le trait d’union conditionnel U+00AD', '\u00AD'],
    ['l’espace de largeur nulle U+200B', '\u200B'],
    ['le trait d’union U+2010', '\u2010'],
    ['le trait d’union insécable U+2011', '\u2011'],
    ['le tiret de chiffre U+2012', '\u2012'],
    ['le tiret demi-cadratin U+2013', '\u2013'],
    ['le tiret cadratin U+2014', '\u2014'],
    ['le signe moins U+2212', '\u2212'],
    ['le tiret pleine chasse U+FF0D', '\uFF0D'],
  ]

  for (const [nom, caractere] of retires) {
    test(`retire ${nom}`, () => {
      assert.equal(normaliserCode(`ABCD${caractere}EFGH`), 'ABCDEFGH')
    })
  }

  test('ne retire pas U+FEFF, que .NET ne tient pas pour une espace', () => {
    // \s de JavaScript l'inclut, char.IsWhiteSpace de .NET non : un code collé avec cette
    // marque serait accepté ici et refusé là, ou l'inverse.
    assert.equal(normaliserCode('AB\uFEFFCD'), 'AB\uFEFFCD')
  })

  test('retire U+0085, que .NET tient pour une espace et pas JavaScript', () => {
    assert.equal(normaliserCode('AB\u0085CD'), 'ABCD')
  })

  test('met en majuscules', () => {
    assert.equal(normaliserCode('abcdefghjkmnpqrstuvw'), 'ABCDEFGHJKMNPQRSTUVW')
  })

  test('ne change pas la longueur d’un caractère qui s’étendrait en majuscules', () => {
    // « ß » devient « SS » par toUpperCase d'une chaîne, pas par ToUpperInvariant d'un char.
    assert.equal(normaliserCode('aßb'), 'Aß'.concat('B'))
  })
})

describe('refusCode', () => {
  test('vingt symboles de l’alphabet partent, sous toute forme collée', () => {
    assert.equal(refusCode(CodeBon), null)
    assert.equal(refusCode('abcd-efgh-jkmn-pqrs-tuvw'), null)
  })

  test('dix-neuf symboles sont refusés', () => {
    assert.match(refusCode(CodeBon.slice(0, 19)) ?? '', /20/)
  })

  test('vingt et un symboles sont refusés', () => {
    assert.match(refusCode(`${CodeBon}A`) ?? '', /20/)
  })

  test('un code vide est refusé', () => {
    assert.notEqual(refusCode(''), null)
  })

  for (const c of ['I', 'L', 'O', '0', '1']) {
    test(`${c} est refusé en disant que les lettres I, L, O et les chiffres 0, 1 n'y figurent pas`, () => {
      assert.match(
        refusCode(`${CodeBon.slice(0, 19)}${c}`) ?? '',
        /les lettres I, L, O et les chiffres 0, 1 n'y figurent pas/,
      )
    })
  }

  for (const c of ['$', 'é']) {
    test(`${c} est refusé comme un caractère qui n'existe pas dans un code`, () => {
      assert.match(refusCode(`${CodeBon.slice(0, 19)}${c}`) ?? '', /n'existe pas dans un code/)
    })
  }

  test('un caractère étranger est annoncé avant la longueur', () => {
    assert.match(refusCode('$') ?? '', /n'existe pas/)
  })

  test('l’alphabet est celui du serveur', () => {
    assert.equal(AlphabetCode, 'ABCDEFGHJKMNPQRSTUVWXYZ23456789')
  })
})

describe('refusEntreprise', () => {
  const bonne = { raisonSociale: 'Durand', siret: '12345678900012', territoire: 'Metropole' }

  test('une saisie complète part', () => {
    assert.equal(refusEntreprise(bonne), null)
  })

  test('une raison sociale vide est refusée', () => {
    assert.notEqual(refusEntreprise({ ...bonne, raisonSociale: '   ' }), null)
  })

  test('une raison sociale de plus de 200 caractères est refusée', () => {
    assert.notEqual(refusEntreprise({ ...bonne, raisonSociale: 'a'.repeat(201) }), null)
    assert.equal(refusEntreprise({ ...bonne, raisonSociale: 'a'.repeat(200) }), null)
  })

  test('un caractère de contrôle est refusé, en français', () => {
    assert.match(refusEntreprise({ ...bonne, raisonSociale: 'Dur\u0000and' }) ?? '', /contrôle/)
  })

  test('un SIRET écrit par groupes d’espaces est accepté', () => {
    assert.equal(refusEntreprise({ ...bonne, siret: '123 456 789 00012' }), null)
  })

  test('un SIRET de quatre chiffres est refusé', () => {
    assert.notEqual(refusEntreprise({ ...bonne, siret: '1234' }), null)
  })

  test('un SIRET écrit avec des points est refusé : le serveur ne retire que les espaces', () => {
    assert.notEqual(refusEntreprise({ ...bonne, siret: '123.456.789.00012' }), null)
  })

  test('un territoire hors de la liste est refusé', () => {
    assert.notEqual(refusEntreprise({ ...bonne, territoire: 'Atlantide' }), null)
    assert.notEqual(refusEntreprise({ ...bonne, territoire: '0' }), null)
  })

  test('les sept territoires sont acceptés', () => {
    for (const t of ['Metropole', 'AlsaceMoselle', 'Guadeloupe', 'Martinique', 'Guyane', 'Reunion', 'Mayotte']) {
      assert.equal(refusEntreprise({ ...bonne, territoire: t }), null, t)
    }
  })
})

describe('refusCompte', () => {
  const bon = {
    email: 'patron@exemple.fr',
    emailConfirmation: 'patron@exemple.fr',
    nom: 'Durand',
    prenom: 'Marie',
    motDePasse: 'Secret2026Secret',
    confirmation: 'Secret2026Secret',
  }

  test('une saisie complète part', () => {
    assert.equal(refusCompte(bon), null)
  })

  for (const mauvaise of ['a@', '@b', 'a b@c.d', 'sans-arobase', '']) {
    test(`l'adresse « ${mauvaise} » est refusée`, () => {
      assert.notEqual(refusCompte({ ...bon, email: mauvaise, emailConfirmation: mauvaise }), null)
    })
  }

  test('l’adresse est rognée avant d’être jugée', () => {
    assert.equal(refusCompte({ ...bon, email: ' patron@exemple.fr ', emailConfirmation: 'patron@exemple.fr' }), null)
  })

  test('deux saisies d’adresse différentes sont refusées', () => {
    assert.match(refusCompte({ ...bon, emailConfirmation: 'autre@exemple.fr' }) ?? '', /adresse/i)
  })

  test('un nom vide, ou de plus de 100 caractères, est refusé', () => {
    assert.notEqual(refusCompte({ ...bon, nom: '' }), null)
    assert.notEqual(refusCompte({ ...bon, nom: 'a'.repeat(101) }), null)
    assert.equal(refusCompte({ ...bon, nom: 'a'.repeat(100) }), null)
  })

  test('un prénom vide, ou de plus de 100 caractères, est refusé', () => {
    assert.notEqual(refusCompte({ ...bon, prenom: '  ' }), null)
    assert.notEqual(refusCompte({ ...bon, prenom: 'a'.repeat(101) }), null)
  })

  test('un caractère de contrôle dans le nom est refusé', () => {
    assert.match(refusCompte({ ...bon, nom: 'Du\u0000rand' }) ?? '', /contrôle/)
  })

  test('un mot de passe vide est refusé', () => {
    assert.notEqual(refusCompte({ ...bon, motDePasse: '', confirmation: '' }), null)
  })

  test('une confirmation de mot de passe différente est refusée', () => {
    assert.match(refusCompte({ ...bon, confirmation: 'Autre2026Secret' }) ?? '', /diffèrent/)
  })

  test('aucune règle de complexité : Identity est l’autorité', () => {
    assert.equal(refusCompte({ ...bon, motDePasse: 'a', confirmation: 'a' }), null)
  })
})

describe('corpsInstallation', () => {
  const corps = corpsInstallation(complete)

  test('le code part normalisé', () => {
    assert.equal(corps.code, 'ABCDEFGHJKMNPQRSTUVW')
  })

  test('le SIRET part sans espaces', () => {
    assert.equal(corps.siret, '12345678900012')
  })

  test('les champs sont rognés', () => {
    assert.equal(corps.raisonSociale, 'Propreté Durand')
    assert.equal(corps.email, 'patron@exemple.fr')
    assert.equal(corps.nom, 'Durand')
    assert.equal(corps.prenom, 'Marie')
  })

  test('le mot de passe part tel quel, sans rognage', () => {
    assert.equal(corpsInstallation({ ...complete, motDePasse: ' a b ' }).motDePasse, ' a b ')
  })

  test('ni la confirmation du mot de passe ni celle de l’adresse ne partent', () => {
    assert.deepEqual(Object.keys(corps).sort(), [
      'code', 'email', 'motDePasse', 'nom', 'prenom', 'raisonSociale', 'siret', 'territoire',
    ])
  })

  test('la saisie vide commence en Métropole', () => {
    assert.equal(SaisieVide.territoire, 'Metropole')
  })
})

describe('refusDInstallation — où renvoyer l’utilisateur', () => {
  test('un 400 de code faux revient au premier écran', () => {
    const r = refusDInstallation(400, `${DebutRefusCode}. Il s'affiche à la fin de l'installation sur le serveur, et par : sudo klean code`)
    assert.equal(r.retourAuCode, true)
    assert.match(r.message, /sudo klean code/)
  })

  test('un 429 qui commence par le refus du code revient au premier écran', () => {
    // Le plafond global des codes faux dit encore que le code est faux.
    const r = refusDInstallation(429, `${DebutRefusCode}, ou trop de codes faux reçus par ce serveur. Vérifiez le code (sudo klean code), puis réessayez dans quelques minutes.`)
    assert.equal(r.retourAuCode, true)
  })

  test('un 400 de champ reste sur l’écran en cours', () => {
    const r = refusDInstallation(400, 'Le SIRET compte 14 chiffres.')
    assert.equal(r.retourAuCode, false)
    assert.equal(r.message, 'Le SIRET compte 14 chiffres.')
  })

  test('un 400 d’Identity sur le mot de passe reste sur l’écran en cours', () => {
    assert.equal(refusDInstallation(400, 'Le mot de passe doit compter 10 caractères.').retourAuCode, false)
  })

  test('un 409 « déjà installé » rend son message, sans retour au code', () => {
    const r = refusDInstallation(409, 'KLEAN est déjà installé : connectez-vous.')
    assert.deepEqual(r, { retourAuCode: false, message: 'KLEAN est déjà installé : connectez-vous.' })
  })

  test('un 409 d’entreprise orpheline rend son message, sans retour au code', () => {
    const r = refusDInstallation(409, 'Une entreprise existe déjà.')
    assert.deepEqual(r, { retourAuCode: false, message: 'Une entreprise existe déjà.' })
  })

  test('un 409 de SIRET pris rend son message', () => {
    assert.equal(refusDInstallation(409, 'Ce SIRET existe déjà.').retourAuCode, false)
  })

  test('un 429 de plafond par adresse rend son message, sans retour au code', () => {
    const r = refusDInstallation(429, 'Trop de tentatives. Réessayer dans quelques minutes.')
    assert.deepEqual(r, { retourAuCode: false, message: 'Trop de tentatives. Réessayer dans quelques minutes.' })
  })

  test('le début du refus n’est cherché qu’au début du message', () => {
    assert.equal(refusDInstallation(400, `Le champ dit : ${DebutRefusCode}`).retourAuCode, false)
  })

  test('un 409 ou un 500 qui commencerait par ce texte ne revient pas au code', () => {
    assert.equal(refusDInstallation(409, DebutRefusCode).retourAuCode, false)
    assert.equal(refusDInstallation(500, DebutRefusCode).retourAuCode, false)
  })
})
