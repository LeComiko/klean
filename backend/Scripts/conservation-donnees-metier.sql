-- Conservation des données métier (RGPD.md §5).
--
-- ⚠️ LES DURÉES CI-DESSOUS SONT DES PROPOSITIONS, PAS DES OBLIGATIONS CONSTATÉES.
-- `RGPD.md` §5 les donne avec leur raison et les marque « à valider juridiquement ».
-- Ce script les applique telles quelles : les changer ici sans les changer là-bas
-- ferait diverger la politique écrite de la politique exécutée, ce qui est pire que
-- l'absence des deux.
--
--   Interventions ................. 5 ans après la date prévue
--   Absences ...................... 5 ans après la date de fin
--   Remplacements ................. 1 an après leur création
--   Contrats, sites, clients ...... 5 ans après la fin du dernier contrat
--   Agents ........................ 5 ans après la fin du contrat de travail,
--                                   et par PSEUDONYMISATION, pas par suppression
--
--     psql -d klean -f backend/Scripts/conservation-donnees-metier.sql
--
-- ⚠️ Lire d'abord. Tous les DELETE sont commentés, et leur ORDRE est la moitié du
-- travail : voir la section finale.

\echo '=== 1. Interventions au-delà de 5 ans ==='

SELECT
    count(*)              AS interventions_a_purger,
    min("DatePrevue")     AS plus_ancienne,
    max("DatePrevue")     AS plus_recente
FROM "Interventions"
WHERE "DatePrevue" < now() - interval '5 years';

\echo ''
\echo '=== 2. Absences au-delà de 5 ans ==='

-- Le détail par type n'est pas décoratif : une absence `Maladie` est une donnée de
-- l'article 9, et `degradation-absences-maladie.sql` doit l'avoir dégradée bien avant
-- cette échéance. Un compte non nul dans la colonne `maladie` ci-dessous signale que
-- ce second script n'a jamais tourné.
SELECT
    "Type"                AS type_absence,
    count(*)              AS absences_a_purger,
    min("DateFin")        AS plus_ancienne,
    max("DateFin")        AS plus_recente
FROM "Absences"
WHERE "DateFin" < now() - interval '5 years'
GROUP BY 1
ORDER BY 1;

\echo ''
\echo '=== 3. Remplacements au-delà d''un an ==='

-- État dérivé, sans valeur probante au-delà de l'exercice : posé par
-- `DetecterImpactsAsync` à partir d'une absence, ou ouvert sur une vacation. Aucun
-- écran ne le liste.
SELECT
    "Statut"              AS statut,
    count(*)              AS remplacements_a_purger
FROM "Remplacements"
WHERE "DateCreation" < now() - interval '1 year'
GROUP BY 1
ORDER BY 1;

\echo ''
\echo '=== 4. Chaîne client : au-delà de 5 ans, et réellement supprimable ? ==='

-- ⚠️ La colonne qui compte est la DERNIÈRE. La chaîne d'exploitation est en
-- `Restrict` depuis l'étape 16 bis : une ligne hors délai dont un enfant subsiste
-- n'est pas supprimable, et un DELETE en masse échouerait à mi-parcours. Les lignes
-- bloquées le restent jusqu'à ce que leurs enfants tombent eux-mêmes hors délai —
-- c'est-à-dire que ce script doit être rejoué, pas forcé.
WITH contrats_echus AS (
    SELECT c."Id", c."SiteId"
    FROM "Contrats" c
    WHERE c."DateFin" IS NOT NULL
      AND c."DateFin" < now() - interval '5 years'
)
SELECT
    'prestations'                                             AS niveau,
    count(*)                                                  AS hors_delai,
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Interventions" i WHERE i."PrestationId" = p."Id")
    )                                                         AS supprimable_maintenant
FROM "Prestations" p
WHERE p."ContratId" IN (SELECT "Id" FROM contrats_echus)

UNION ALL

SELECT
    'contrats',
    count(*),
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Prestations" p WHERE p."ContratId" = c."Id")
    )
FROM "Contrats" c
WHERE c."DateFin" IS NOT NULL
  AND c."DateFin" < now() - interval '5 years'

UNION ALL

-- Un site n'a pas de date propre : il est hors délai quand TOUS ses contrats le sont,
-- et il n'en faut aucun en cours. Un site sans aucun contrat n'est jamais compté ici —
-- il n'a pas de date de fin dont partir, et c'est une donnée à traiter à la main.
SELECT
    'sites',
    count(*),
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Contrats" c2 WHERE c2."SiteId" = s."Id")
    )
FROM "Sites" s
WHERE EXISTS (SELECT 1 FROM "Contrats" c3 WHERE c3."SiteId" = s."Id")
  AND NOT EXISTS (
      SELECT 1 FROM "Contrats" c4
      WHERE c4."SiteId" = s."Id"
        AND (c4."DateFin" IS NULL OR c4."DateFin" >= now() - interval '5 years')
  )

UNION ALL

SELECT
    'clients',
    count(*),
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Sites" s2 WHERE s2."ClientId" = cl."Id")
    )
FROM "Clients" cl
WHERE EXISTS (SELECT 1 FROM "Sites" s3 WHERE s3."ClientId" = cl."Id")
  AND NOT EXISTS (
      SELECT 1
      FROM "Sites" s4
      JOIN "Contrats" c5 ON c5."SiteId" = s4."Id"
      WHERE s4."ClientId" = cl."Id"
        AND (c5."DateFin" IS NULL OR c5."DateFin" >= now() - interval '5 years')
  );

\echo ''
\echo '=== 5. Sites et clients sans aucun contrat — à traiter à la main ==='

-- Aucune date dont partir : ni création, ni fin. Ce sont des saisies abandonnées, ou
-- des prospects. Le registre doit en dire quelque chose, et le script ne peut pas
-- décider pour lui.
SELECT 'sites sans contrat'  AS cas, count(*) AS lignes
FROM "Sites" s WHERE NOT EXISTS (SELECT 1 FROM "Contrats" c WHERE c."SiteId" = s."Id")
UNION ALL
SELECT 'clients sans site', count(*)
FROM "Clients" cl WHERE NOT EXISTS (SELECT 1 FROM "Sites" s WHERE s."ClientId" = cl."Id");

\echo ''
\echo '=== 6. Agents : pseudonymisation, jamais suppression ==='

-- ⚠️ Aucun DELETE sur `Agents` dans ce script, et c'est le cœur du compromis de
-- l'étape 17 : `Restrict` sur `Intervention.AgentId` rend un agent insupprimable dès
-- qu'il a travaillé, ce qui est le bon choix pour l'intégrité du planning. La réponse
-- à une demande d'effacement est `POST /api/agents/{id}/anonymiser`, qui conserve la
-- ligne et remplace ce qui identifie.
--
-- `DateEmbauche` ne dit pas la fin du contrat de travail — le modèle ne la porte pas.
-- `Actif = false` est le seul signal disponible, et il ne porte aucune date : la
-- colonne « inactifs, non pseudonymisés » ci-dessous est donc une liste à examiner,
-- pas une échéance.
SELECT
    count(*) FILTER (WHERE "Actif")                                         AS actifs,
    count(*) FILTER (WHERE NOT "Actif" AND "DateAnonymisation" IS NULL)     AS inactifs_a_examiner,
    count(*) FILTER (WHERE "DateAnonymisation" IS NOT NULL)                 AS deja_pseudonymises,
    count(*) FILTER (WHERE "Latitude" IS NOT NULL AND NOT "Actif")          AS inactifs_gardant_leurs_coordonnees
FROM "Agents";

\echo ''
\echo '=== 7. Agents pseudonymisés depuis plus de 5 ans ==='

-- À ce stade seulement la suppression devient envisageable : la ligne ne porte plus
-- rien d''identifiant. Elle reste bloquée par ses interventions, et c''est bien ainsi
-- tant qu''elles sont dans leur propre délai.
SELECT
    count(*)                                     AS agents_pseudonymises_hors_delai,
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Interventions" i WHERE i."AgentId" = a."Id")
         AND  NOT EXISTS (SELECT 1 FROM "Absences" ab WHERE ab."AgentId" = a."Id")
         AND  NOT EXISTS (SELECT 1 FROM "Remplacements" r WHERE r."AgentRemplacantId" = a."Id")
    )                                            AS supprimable_maintenant
FROM "Agents" a
WHERE a."DateAnonymisation" IS NOT NULL
  AND a."DateAnonymisation" < now() - interval '5 years';

-- ---------------------------------------------------------------------------
-- La purge, commentée. L'ORDRE EST LA MOITIÉ DU TRAVAIL.
-- ---------------------------------------------------------------------------
--
-- Six clés étrangères de la chaîne d'exploitation sont en `Restrict` depuis la
-- migration `SuppressionRestreinteSurLaChaineExploitation`, et deux de plus le sont
-- sur l'agent — `Intervention.AgentId` et `Remplacement.AgentRemplacantId`. Un DELETE
-- dans le mauvais ordre échoue sur une violation de clé, à mi-parcours, après avoir
-- déjà supprimé ce qui précédait. Lire l'ordre ci-dessous comme une contrainte, pas
-- comme une préférence de présentation.
--
-- `Remplacement` est la seule exception en `Cascade` : supprimer une intervention ou
-- une absence emporte ses remplacements. Le DELETE 1 ne sert donc qu'à purger les
-- remplacements dont le parent est encore dans son délai.
--
-- Chaque DELETE est atomique de lui-même. Aucune transaction englobante : elle
-- donnerait l'illusion qu'on peut défaire l'ensemble après coup, alors que le choix
-- utile est de rejouer le script.
--
-- -- 1. Remplacements (état dérivé)
-- DELETE FROM "Remplacements" WHERE "DateCreation" < now() - interval '1 year';
--
-- -- 2. Interventions (cascade sur leurs remplacements)
-- DELETE FROM "Interventions" WHERE "DatePrevue" < now() - interval '5 years';
--
-- -- 3. Absences (cascade sur leurs remplacements)
-- --    ⚠️ Vérifier d'abord que `degradation-absences-maladie.sql` a tourné : une
-- --    absence `Maladie` encore qualifiée ici a passé cinq ans en donnée de santé.
-- DELETE FROM "Absences" WHERE "DateFin" < now() - interval '5 years';
--
-- -- 4. Prestations devenues sans intervention
-- DELETE FROM "Prestations" p
-- WHERE p."ContratId" IN (
--         SELECT "Id" FROM "Contrats"
--         WHERE "DateFin" IS NOT NULL AND "DateFin" < now() - interval '5 years')
--   AND NOT EXISTS (SELECT 1 FROM "Interventions" i WHERE i."PrestationId" = p."Id");
--
-- -- 5. Contrats devenus sans prestation
-- DELETE FROM "Contrats" c
-- WHERE c."DateFin" IS NOT NULL AND c."DateFin" < now() - interval '5 years'
--   AND NOT EXISTS (SELECT 1 FROM "Prestations" p WHERE p."ContratId" = c."Id");
--
-- -- 6. Sites devenus sans contrat, qui en avaient
-- DELETE FROM "Sites" s
-- WHERE NOT EXISTS (SELECT 1 FROM "Contrats" c WHERE c."SiteId" = s."Id")
--   AND EXISTS (SELECT 1 FROM "Interventions" x WHERE false);  -- garde-fou : voir ci-dessous
--
-- --    ⚠️ Le garde-fou ci-dessus est volontairement toujours faux, donc ce DELETE ne
-- --    supprime rien tel quel. Raison : « site sans contrat » ne distingue pas un site
-- --    dont les contrats ont expiré et été purgés d'un site saisi hier et pas encore
-- --    sous contrat. Le modèle ne porte pas de date de création sur `Sites`, donc la
-- --    distinction n'est pas calculable. Les lignes de la section 5 ci-dessus sont à
-- --    traiter à la main, ou le modèle doit gagner une `DateCreation`.
--
-- -- 7. Clients : même réserve exactement.
--
-- -- 8. Agents : AUCUN DELETE. Voir la section 6.
