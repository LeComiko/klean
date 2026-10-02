import assert from 'node:assert/strict'
import { describe, test } from 'node:test'

// ⚠️ Extension explicite : `node --test` n'applique pas la résolution sans
// extension de Vite (CLAUDE.md §10).
import { refusLocal } from './motDePasse.ts'

/**
 * Les deux refus locaux du changement de mot de passe.
 *
 * Ce qu'ils gardent n'est pas du confort : ce sont les deux seuls cas du parcours où
 * le serveur répondrait en anglais, la validation de modèle d'ASP.NET s'exécutant
 * avant Identity. Les retirer ne casse rien de visible — l'appel partirait, le
 * serveur refuserait — et l'utilisateur francophone lirait « The MotDePasseActuel
 * field is required. » sous un formulaire entièrement en français.
 */
describe('refusLocal — ce que l’écran refuse avant d’appeler', () => {
  test('une saisie complète et cohérente part', () => {
    assert.equal(refusLocal('AncienSecret2026', 'NouveauSecret2026', 'NouveauSecret2026'), null)
  })

  describe('les champs obligatoires', () => {
    test('le mot de passe actuel manquant est refusé', () => {
      assert.match(refusLocal('', 'NouveauSecret2026', 'NouveauSecret2026') ?? '', /obligatoires/)
    })

    test('le nouveau mot de passe manquant est refusé', () => {
      assert.match(refusLocal('AncienSecret2026', '', '') ?? '', /obligatoires/)
    })

    test('un formulaire vide parle d’absence, pas de discordance', () => {
      const refus = refusLocal('', '', '')

      assert.match(refus ?? '', /obligatoires/)
      assert.doesNotMatch(refus ?? '', /diffèrent/)
    })

    test('un champ manquant est annoncé avant une confirmation discordante', () => {
      // ⚠️ Le seul cas où l'ordre des deux contrôles décide, et le seul qui le
      // garde. Sur un formulaire entièrement vide les deux ordres disent
      // « obligatoires » — des champs vides sont aussi des champs identiques — donc
      // le test précédent laissait passer une inversion, ce qu'un sabotage a montré.
      // Il faut un champ absent ET une confirmation différente.
      const refus = refusLocal('', 'NouveauSecret2026', 'AutreSecret2026')

      assert.match(refus ?? '', /obligatoires/)
      assert.doesNotMatch(refus ?? '', /diffèrent/)
    })

    test('la confirmation seule vide est lue comme une discordance', () => {
      // Elle n'est pas dans la liste des obligatoires, et c'est volontaire : son
      // seul rôle est d'égaler le nouveau mot de passe. Vide, elle ne l'égale pas.
      assert.match(refusLocal('AncienSecret2026', 'NouveauSecret2026', '') ?? '', /diffèrent/)
    })
  })

  describe('la confirmation', () => {
    test('deux saisies différentes sont refusées', () => {
      assert.match(
        refusLocal('AncienSecret2026', 'NouveauSecret2026', 'NouveauSecret2027') ?? '',
        /diffèrent/,
      )
    })

    test('la comparaison est exacte, casse comprise', () => {
      assert.notEqual(refusLocal('A', 'Secret2026', 'secret2026'), null)
    })

    test('une différence d’espace en fin de saisie est une différence', () => {
      // C'est le cas le plus utile de ce contrôle : invisible à l'œil dans un champ
      // de type password, et fatal — l'utilisateur enregistrerait un mot de passe
      // qu'il ne saurait pas retaper.
      assert.match(
        refusLocal('AncienSecret2026', 'NouveauSecret2026', 'NouveauSecret2026 ') ?? '',
        /diffèrent/,
      )
    })
  })

  describe('ce que ce module ne juge pas', () => {
    test('une espace seule n’est pas une absence', () => {
      // ⚠️ Aucun `trim` : une espace est un caractère de mot de passe valide, et
      // l'autorité sur les caractères acceptés est Identity. Rogner ici refuserait
      // un mot de passe que le serveur accepte — ou en modifierait un avant envoi.
      assert.equal(refusLocal(' ', ' ', ' '), null)
    })

    test('un nouveau mot de passe trop court part quand même', () => {
      // La longueur minimale appartient à Identity, qui parle français : la
      // dupliquer ici créerait deux règles destinées à diverger (CLAUDE.md §6).
      assert.equal(refusLocal('AncienSecret2026', 'a', 'a'), null)
    })

    test('réutiliser le mot de passe actuel part quand même', () => {
      // Le serveur ne l'interdit pas. Inventer la règle ici la rendrait invisible
      // à toute relecture du backend, et ferait refuser un geste légitime : changer
      // pour la même valeur régénère l'empreinte de sécurité, donc déconnecte les
      // autres appareils.
      assert.equal(refusLocal('MemeSecret2026', 'MemeSecret2026', 'MemeSecret2026'), null)
    })
  })
})
