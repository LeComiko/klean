import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

import {
  ajouterJours,
  depuisChaineIso,
  formaterDateLongue,
  formaterDuree,
  formaterHeure,
  formaterHeures,
  formaterPlage,
  formaterPlageDeSemaine,
  jourCourt,
  jourIso,
  jourLong,
  lundiDeLaSemaine,
  numeroSemaine,
  versChaineIso,
} from './dates.ts'

/**
 * ⚠️ Cette série doit tourner dans **plusieurs fuseaux**, et c'est le lanceur
 * `scripts/tests.mjs` qui s'en charge. Les deux pièges de date de `CLAUDE.md`
 * §10 sont symétriques : `toISOString` casse aux décalages **positifs**,
 * `new Date('AAAA-MM-JJ')` aux décalages **négatifs**. Les deux sabotages ont été
 * joués, et aucun des deux ne se voit sous `TZ=UTC`. Une exécution en UTC seul
 * validerait donc un code faux dans les deux sens.
 *
 * Le fuseau courant est affiché dans le nom du groupe : un échec dit tout de
 * suite lequel.
 */
const fuseau = process.env.TZ ?? '(système)'

describe(`dates — TZ=${fuseau}`, () => {
  describe('versChaineIso', () => {
    test('rend le jour local, pas le jour UTC', () => {
      // Minuit local le 7 septembre. `toISOString()` rendrait le 6 à tout
      // décalage positif : c'est exactement le piège que cette fonction évite.
      assert.equal(versChaineIso(new Date(2026, 8, 7)), '2026-09-07')
    })

    test('complète les mois et les jours à deux chiffres', () => {
      assert.equal(versChaineIso(new Date(2026, 0, 1)), '2026-01-01')
      assert.equal(versChaineIso(new Date(2026, 11, 31)), '2026-12-31')
    })

    test('reste stable de part et d’autre d’un changement d’heure', () => {
      // Europe : passage à l'heure d'été le 29 mars 2026, retour le 25 octobre.
      assert.equal(versChaineIso(new Date(2026, 2, 28)), '2026-03-28')
      assert.equal(versChaineIso(new Date(2026, 2, 29)), '2026-03-29')
      assert.equal(versChaineIso(new Date(2026, 2, 30)), '2026-03-30')
      assert.equal(versChaineIso(new Date(2026, 9, 25)), '2026-10-25')
    })

    test('fait un aller-retour avec depuisChaineIso', () => {
      for (const iso of ['2026-01-01', '2026-03-29', '2026-09-07', '2026-10-25', '2026-12-31']) {
        assert.equal(versChaineIso(depuisChaineIso(iso)), iso)
      }
    })
  })

  describe('depuisChaineIso', () => {
    test('construit une date locale, pas une date UTC', () => {
      const date = depuisChaineIso('2026-09-07')

      // `new Date('2026-09-07')` serait lu comme de l'UTC et tomberait le 6 au
      // soir à tout décalage négatif — la grille du planning décalerait d'un jour.
      assert.equal(date.getFullYear(), 2026)
      assert.equal(date.getMonth(), 8)
      assert.equal(date.getDate(), 7)
      assert.equal(date.getHours(), 0)
    })
  })

  describe('lundiDeLaSemaine', () => {
    test('les sept jours d’une semaine rendent le même lundi', () => {
      for (let decalage = 0; decalage < 7; decalage++) {
        assert.equal(
          versChaineIso(lundiDeLaSemaine(new Date(2026, 8, 7 + decalage))),
          '2026-09-07',
          `décalage ${String(decalage)}`,
        )
      }
    })

    test('un dimanche appartient à la semaine qui commence le lundi précédent', () => {
      // Le piège de getDay() : dimanche vaut 0, donc un calcul naïf le rattache
      // au lundi suivant.
      assert.equal(versChaineIso(lundiDeLaSemaine(new Date(2026, 8, 6))), '2026-08-31')
      assert.equal(versChaineIso(lundiDeLaSemaine(new Date(2026, 8, 13))), '2026-09-07')
    })

    test('est idempotente sur un lundi', () => {
      const lundi = new Date(2026, 8, 7)

      assert.equal(
        versChaineIso(lundiDeLaSemaine(lundiDeLaSemaine(lundi))),
        versChaineIso(lundi),
      )
    })

    test('traverse un début de mois et un début d’année', () => {
      assert.equal(versChaineIso(lundiDeLaSemaine(new Date(2026, 8, 2))), '2026-08-31')
      assert.equal(versChaineIso(lundiDeLaSemaine(new Date(2026, 0, 1))), '2025-12-29')
    })
  })

  describe('numeroSemaine', () => {
    test('la semaine de référence du jeu de démonstration', () => {
      assert.equal(numeroSemaine(new Date(2026, 8, 7)), 37)
    })

    test('respecte la règle ISO du premier jeudi', () => {
      // Compter depuis le 1er janvier donnerait un numéro faux une année sur
      // deux. La semaine 1 est celle qui contient le premier jeudi.
      assert.equal(numeroSemaine(new Date(2026, 0, 1)), 1, '1er janvier 2026, un jeudi')
      assert.equal(numeroSemaine(new Date(2025, 11, 29)), 1, 'lundi de la S1 de 2026')
      assert.equal(numeroSemaine(new Date(2024, 11, 30)), 1, 'lundi de la S1 de 2025')
      assert.equal(numeroSemaine(new Date(2021, 0, 1)), 53, 'vendredi de la S53 de 2020')
      assert.equal(numeroSemaine(new Date(2027, 0, 3)), 53, 'dimanche de la S53 de 2026')
    })

    test('les sept jours d’une semaine portent le même numéro', () => {
      for (let decalage = 0; decalage < 7; decalage++) {
        assert.equal(numeroSemaine(new Date(2026, 8, 7 + decalage)), 37)
      }
    })

    test('avance d’exactement un par semaine, y compris sur un changement d’heure', () => {
      // Le quotient est arrondi et non tronqué : une heure de décalage sur une
      // différence de sept jours ferait sinon tomber le numéro d'une unité. Les
      // deux passages de 2026 — 29 mars et 25 octobre — sont dans l'intervalle.
      //
      // La boucle s'arrête à la dernière semaine de 2026 : au-delà le numéro ISO
      // repart à 1 et l'incrément n'a plus de sens, ce qui est l'objet du test
      // suivant. Ma première version bouclait quarante-quatre fois et attendait
      // une « semaine 54 » qui n'existe pas.
      const depart = new Date(2026, 2, 2) // lundi de la semaine 10
      let precedent = numeroSemaine(depart)

      assert.equal(precedent, 10)

      for (let semaine = 1; semaine <= 43; semaine++) {
        const lundi = new Date(2026, 2, 2 + semaine * 7)
        const numero = numeroSemaine(lundi)

        assert.equal(
          numero,
          precedent + 1,
          `semaine du ${versChaineIso(lundi)} : ${String(numero)} après ${String(precedent)}`,
        )
        precedent = numero
      }

      assert.equal(precedent, 53, '2026 compte cinquante-trois semaines ISO')
    })

    test('repart à 1 au passage d’année, sans inventer de semaine 54', () => {
      assert.equal(numeroSemaine(new Date(2026, 11, 28)), 53, 'lundi de la S53 de 2026')
      assert.equal(numeroSemaine(new Date(2027, 0, 4)), 1, 'lundi suivant, S1 de 2027')
    })
  })

  describe('ajouterJours', () => {
    test('traverse les changements d’heure sans perdre de jour', () => {
      assert.equal(versChaineIso(ajouterJours(new Date(2026, 2, 28), 2)), '2026-03-30')
      assert.equal(versChaineIso(ajouterJours(new Date(2026, 9, 24), 2)), '2026-10-26')
    })

    test('accepte un décalage négatif et traverse les mois', () => {
      assert.equal(versChaineIso(ajouterJours(new Date(2026, 8, 7), -7)), '2026-08-31')
      assert.equal(versChaineIso(ajouterJours(new Date(2026, 0, 4), -7)), '2025-12-28')
    })

    test('les cinq colonnes ouvrées de la semaine de référence', () => {
      const lundi = depuisChaineIso('2026-09-07')

      assert.deepEqual(
        [0, 1, 2, 3, 4].map((decalage) => versChaineIso(ajouterJours(lundi, decalage))),
        ['2026-09-07', '2026-09-08', '2026-09-09', '2026-09-10', '2026-09-11'],
      )
    })
  })

  describe('jourIso', () => {
    test('extrait le jour d’un horodatage du serveur sans passer par Date', () => {
      // Le serveur envoie minuit UTC. L'extraction textuelle est insensible au
      // fuseau, ce qu'aucune conversion en Date ne peut garantir.
      assert.equal(jourIso('2026-09-07T00:00:00Z'), '2026-09-07')
      assert.equal(jourIso('2026-09-09T00:00:00Z'), '2026-09-09')
      assert.equal(jourIso('2026-12-31T00:00:00.0000000Z'), '2026-12-31')
    })
  })

  describe('formatage', () => {
    test('formaterHeure', () => {
      assert.equal(formaterHeure('08:00:00'), '08:00')
      assert.equal(formaterHeure('16:30:00'), '16:30')
    })

    test('formaterPlage', () => {
      assert.equal(sansInsecable(formaterPlage('08:00:00', 120)), '08:00 – 10:00')
      assert.equal(sansInsecable(formaterPlage('16:30:00', 90)), '16:30 – 18:00')
      assert.equal(sansInsecable(formaterPlage('08:00:00', 180)), '08:00 – 11:00')
    })

    test('formaterPlage passe minuit sans rendre 24 ou 25 heures', () => {
      assert.equal(sansInsecable(formaterPlage('23:00:00', 180)), '23:00 – 02:00')
      assert.equal(sansInsecable(formaterPlage('22:30:00', 90)), '22:30 – 00:00')
    })

    test('formaterPlage garde un espace insécable avant le tiret', () => {
      // Sans lui, la plage se coupe en fin de ligne dans un bloc étroit.
      assert.ok(formaterPlage('08:00:00', 120).includes(' '))
    })

    test('formaterDuree, les trois durées du jeu de démonstration', () => {
      assert.equal(formaterDuree(120), '2 h')
      assert.equal(formaterDuree(180), '3 h')
      assert.equal(formaterDuree(90), '1 h 30')
    })

    test('formaterDuree sous l’heure et sur un reste à un chiffre', () => {
      assert.equal(formaterDuree(45), '45 min')
      assert.equal(formaterDuree(0), '0 min')
      assert.equal(formaterDuree(65), '1 h 05')
    })

    test('formaterHeures écrit la virgule décimale française', () => {
      assert.equal(formaterHeures(9.5), '9,5 h')
      assert.equal(formaterHeures(6), '6 h')
      assert.equal(formaterHeures(34), '34 h')
      assert.equal(formaterHeures(7.25), '7,25 h')
    })

    test('formaterDateLongue et les noms de mois français', () => {
      // Écrits à la main : les données ICU du navigateur ne peuvent pas être
      // supposées présentes, et le backend a tranché dans le même sens.
      assert.equal(formaterDateLongue(new Date(2026, 8, 7)), '7 septembre 2026')
      assert.equal(formaterDateLongue(new Date(2026, 7, 31)), '31 août 2026')
      assert.equal(formaterDateLongue(new Date(2026, 1, 1)), '1 février 2026')
    })

    test('formaterPlageDeSemaine ne répète pas un mois identique', () => {
      assert.equal(
        formaterPlageDeSemaine(new Date(2026, 8, 7), new Date(2026, 8, 13)),
        '7 – 13 septembre 2026',
      )
    })

    test('formaterPlageDeSemaine nomme les deux mois à cheval', () => {
      assert.equal(
        formaterPlageDeSemaine(new Date(2026, 7, 31), new Date(2026, 8, 6)),
        '31 août – 6 septembre 2026',
      )
      assert.equal(
        formaterPlageDeSemaine(new Date(2025, 11, 29), new Date(2026, 0, 4)),
        '29 décembre – 4 janvier 2026',
      )
    })

    test('les noms de jours, lundi en tête', () => {
      assert.deepEqual(
        [0, 1, 2, 3, 4].map(jourCourt),
        ['lun.', 'mar.', 'mer.', 'jeu.', 'ven.'],
      )
      assert.equal(jourLong(0), 'lundi')
      assert.equal(jourLong(6), 'dimanche')
    })
  })
})

function sansInsecable(texte: string): string {
  return texte.replace(/ /g, ' ')
}
