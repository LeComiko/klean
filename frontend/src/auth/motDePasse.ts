/**
 * Les refus que l'écran de compte prononce **avant** d'appeler le serveur.
 *
 * ⚠️ Ce module n'existe pas pour épargner un aller-retour réseau : il existe parce
 * que les deux cas qu'il attrape sont les seuls du parcours de mot de passe où le
 * serveur répondrait **en anglais**.
 *
 * La validation de modèle d'ASP.NET s'exécute avant le contrôleur et avant Identity.
 * Un champ vide déclenche donc `[Required]`, qui rend « The MotDePasseActuel field is
 * required. » — un texte que `DescripteurErreursFrancais` ne peut pas atteindre et
 * qu'aucun écran ne peut retraduire (CLAUDE.md §10). Tout le reste — longueur,
 * majuscule, chiffre, mot de passe actuel faux — est jugé par Identity, qui parle
 * français, et son message s'affiche tel quel.
 *
 * La confirmation, elle, n'existe que dans l'écran : le serveur ne la reçoit pas et
 * n'a aucune raison de la recevoir. Elle protège d'une faute de frappe sur une valeur
 * qu'on ne relit pas.
 *
 * Extrait d'un `.tsx` pour la même raison que `reference/valeurs.ts` (CLAUDE.md §11) :
 * enfermée dans un composant, cette logique n'est pas éprouvable, et c'est la seule
 * partie de l'écran qui décide de **ne pas** appeler le serveur.
 */

/**
 * Le motif de refus local, ou `null` si la saisie peut partir.
 *
 * ⚠️ Comparaisons à la chaîne vide, sans `trim`. Une espace est un caractère de mot
 * de passe parfaitement valide, et l'autorité sur les caractères acceptés est
 * Identity, pas cet écran. Rogner la saisie ici refuserait un mot de passe que le
 * serveur accepte — ou, pire, en modifierait un avant de l'envoyer.
 */
export function refusLocal(
  actuel: string,
  nouveau: string,
  confirmation: string,
): string | null {
  // ⚠️ L'absence passe avant la discordance, et le cas où cet ordre compte est plus
  // étroit qu'il n'y paraît : sur un formulaire **entièrement** vide les deux ordres
  // disent « obligatoires », les champs vides étant aussi des champs identiques. Il
  // ne décide que lorsqu'un champ manque **et** que la confirmation diffère — un
  // nouveau mot de passe effacé sous une confirmation restée remplie, par exemple.
  // Annoncer alors « les deux saisies diffèrent » enverrait corriger le champ qui
  // n'est pas en cause. Un sabotage qui inversait les deux est resté vert avant que
  // le test de ce cas précis existe.
  if (actuel === '' || nouveau === '') {
    return 'Le mot de passe actuel et le nouveau sont tous deux obligatoires.'
  }

  if (nouveau !== confirmation) {
    return 'Les deux saisies du nouveau mot de passe diffèrent.'
  }

  return null
}
