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
--   Contrats, sites, clients ...... 5 ans après la fin du dernier contrat ;
--                                   à défaut de contrat, 5 ans après la création
--   Agents ........................ 5 ans après la fin du contrat de travail,
--                                   et par PSEUDONYMISATION, pas par suppression
--   Mouvements de stock ........... 5 ans après la date du mouvement
--   Checklist d'un passage ........ 1 an après la date prévue de la vacation
--   Anomalies ..................... 1 an après le signalement, une fois traitées
--   Photos d'anomalie ............. 3 mois après le traitement de leur anomalie
--
-- Les POINTAGES n'y sont pas : leur durée d'un an est retenue (RGPD.md §5, T7), et ils
-- ont leur propre script, qui purge réellement — `purge-pointages.sql`.
--
--     psql -d klean -f backend/Scripts/conservation-donnees-metier.sql
--
-- ⚠️ Lire d'abord. Tous les DELETE sont commentés, et leur ORDRE est la moitié du
-- travail : voir la section finale.

\echo '=== 1. Interventions au-delà de 5 ans ==='

-- Depuis l'étape 25, trois tables tiennent à la vacation par `Restrict` : ses
-- pointages, sa checklist, ses anomalies. Elles se purgent à un an, donc bien avant ;
-- une vacation que l'une d'elles retient encore signale un script qui n'a pas tourné,
-- ou une anomalie jamais traitée (section 9).
SELECT
    count(*)              AS interventions_a_purger,
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Pointages" po WHERE po."InterventionId" = i."Id")
          AND NOT EXISTS (SELECT 1 FROM "ControlesEffectues" ce WHERE ce."InterventionId" = i."Id")
          AND NOT EXISTS (SELECT 1 FROM "Anomalies" an WHERE an."InterventionId" = i."Id")
    )                     AS supprimable_maintenant,
    min("DatePrevue")     AS plus_ancienne,
    max("DatePrevue")     AS plus_recente
FROM "Interventions" i
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

-- Un site est hors délai quand aucun de ses contrats n'est en cours ni échu depuis
-- moins de 5 ans, ET qu'il a été créé il y a plus de 5 ans. La seconde condition est
-- celle qui manquait avant `DateCreation` (RGPD.md §9.7) : sans elle, un site saisi
-- hier et pas encore sous contrat ressemblait trait pour trait à un site dont les
-- contrats ont été purgés. Pour un site qui a eu des contrats, elle ne retire rien —
-- il a été créé avant leur échéance.
--
-- ⚠️ Avant `DateCreation`, la colonne `supprimable_maintenant` valait TOUJOURS zéro
-- sur cette ligne et la suivante : elle exigeait à la fois qu'un contrat existe (pour
-- avoir une date) et qu'il n'en existe aucun (pour être supprimable). C'était §9.7
-- sous une autre forme.
--
-- Depuis l'étape 22, `Stocks.SiteId` et les quatre lieux de `MouvementsStock` sont
-- des clés étrangères en Restrict : la base refuserait le DELETE d'un site qui porte
-- du stock ou que cite un mouvement. La garde reste écrite ici pour que le rapport
-- ne compte pas comme supprimable ce que le DELETE refuserait — le motif de la
-- colonne `supprimable_maintenant`.
SELECT
    'sites',
    count(*),
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Contrats" c2 WHERE c2."SiteId" = s."Id")
          AND NOT EXISTS (SELECT 1 FROM "Stocks" st WHERE st."SiteId" = s."Id")
          AND NOT EXISTS (
              SELECT 1 FROM "MouvementsStock" m
              WHERE m."OrigineSiteId" = s."Id" OR m."DestinationSiteId" = s."Id")
    )
FROM "Sites" s
WHERE s."DateCreation" < now() - interval '5 years'
  AND NOT EXISTS (
      SELECT 1 FROM "Contrats" c4
      WHERE c4."SiteId" = s."Id"
        AND (c4."DateFin" IS NULL OR c4."DateFin" >= now() - interval '5 years')
  )

UNION ALL

-- Même règle, un niveau plus haut : aucun site du client sous un contrat en cours ou
-- récent, aucun site créé depuis moins de 5 ans, et le client lui-même créé il y a
-- plus de 5 ans.
--
-- `AspNetUsers.ClientId` n'a pas de clé étrangère non plus : un compte `ClientSyndic`
-- rattaché à un client supprimé ne verrait plus rien, sans qu'aucune erreur le dise.
-- Il faut d'abord pseudonymiser ce compte (`POST /api/auth/comptes/{id}/anonymiser`).
SELECT
    'clients',
    count(*),
    count(*) FILTER (
        WHERE NOT EXISTS (SELECT 1 FROM "Sites" s2 WHERE s2."ClientId" = cl."Id")
          AND NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."ClientId" = cl."Id")
    )
FROM "Clients" cl
WHERE cl."DateCreation" < now() - interval '5 years'
  AND NOT EXISTS (
      SELECT 1 FROM "Sites" s3
      WHERE s3."ClientId" = cl."Id"
        AND s3."DateCreation" >= now() - interval '5 years'
  )
  AND NOT EXISTS (
      SELECT 1
      FROM "Sites" s4
      JOIN "Contrats" c5 ON c5."SiteId" = s4."Id"
      WHERE s4."ClientId" = cl."Id"
        AND (c5."DateFin" IS NULL OR c5."DateFin" >= now() - interval '5 years')
  );

\echo ''
\echo '=== 5. Sites et clients sans aucun contrat, par ancienneté ==='

-- Des prospects, des saisies abandonnées, ou des reliquats dont les contrats ont été
-- purgés. Ceux de plus de 5 ans sont déjà comptés en section 4 ; ceux de moins de 5
-- ans sont dans leur délai.
--
-- ⚠️ Les lignes antérieures à la migration `DateCreationSurSitesEtClients` sont
-- datées du jour de cette migration, faute de mieux : c'est une borne haute de leur
-- vraie création, donc leur purge est retardée, jamais avancée.
SELECT
    'sites sans contrat'                                                         AS cas,
    count(*) FILTER (WHERE s."DateCreation" >= now() - interval '5 years')      AS dans_le_delai,
    count(*) FILTER (WHERE s."DateCreation" <  now() - interval '5 years')      AS hors_delai
FROM "Sites" s
WHERE NOT EXISTS (SELECT 1 FROM "Contrats" c WHERE c."SiteId" = s."Id")
UNION ALL
SELECT
    'clients sans site',
    count(*) FILTER (WHERE cl."DateCreation" >= now() - interval '5 years'),
    count(*) FILTER (WHERE cl."DateCreation" <  now() - interval '5 years')
FROM "Clients" cl
WHERE NOT EXISTS (SELECT 1 FROM "Sites" s WHERE s."ClientId" = cl."Id");

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

\echo ''
\echo '=== 8. Mouvements de stock au-delà de 5 ans ==='

-- La seule donnée personnelle d'un mouvement est son auteur (`AuteurId`, RGPD.md §3) :
-- qui a sorti, transféré, compté quoi, et quand. L'identifiant d'un compte, sans clé
-- étrangère ; le nom se lit sur `AspNetUsers`, et suit donc le compte — pseudonymisé
-- au départ de la personne par `POST /api/auth/comptes/{id}/anonymiser`.
--
-- ⚠️ Purger un mouvement ne change AUCUNE quantité. `Stocks.Quantite` est tenue par
-- le service à chaque mouvement, elle n'est pas recalculée depuis l'historique : ce
-- qui disparaît est l'explication des quantités d'il y a cinq ans, pas les quantités.
-- Un inventaire récent reste le point de départ lisible.
--
-- La dernière colonne compte les auteurs de ces mouvements dont le compte n'a jamais
-- été pseudonymisé : ce sont les mouvements qui désignent encore quelqu'un par son
-- nom, à travers le compte.
SELECT
    m."Nature"                                         AS nature,
    count(*)                                           AS mouvements_a_purger,
    min(m."DateMouvement")                             AS plus_ancien,
    count(DISTINCT m."AuteurId") FILTER (
        WHERE EXISTS (
            SELECT 1 FROM "AspNetUsers" u
            WHERE u."Id" = m."AuteurId" AND u."DateAnonymisation" IS NULL)
    )                                                  AS auteurs_encore_identifiables
FROM "MouvementsStock" m
WHERE m."DateMouvement" < now() - interval '5 years'
GROUP BY 1
ORDER BY 1;

\echo ''
\echo '=== 9. Le compte rendu des passages : checklist, anomalies, photos ==='

-- Le détail d'un passage vit un an ; son statut, `Terminee` ou `NonCertifiee`, reste
-- cinq ans sur la vacation. Ce qui a été coché et ce qui a été signalé n'a plus d'usage
-- au-delà d'une contestation du client sur l'exercice.
--
-- ⚠️ Une anomalie encore OUVERTE n'est jamais purgée : elle décrit un dégât ou une fuite
-- que personne n'a traité, et la supprimer effacerait le travail à faire, pas une
-- trace. La colonne `ouvertes_hors_delai` est donc une liste à traiter à l'écran
-- *Anomalies*, pas une échéance — et une vacation que l'une d'elles retient ne se
-- purgera pas à cinq ans (section 1).
--
-- ⚠️ Les photos partent bien avant leur anomalie, trois mois après son traitement. Une
-- photo peut montrer une personne — un occupant, l'agent lui-même —, c'est la donnée la
-- plus lourde du passage, et elle a servi une fois l'anomalie traitée. La description
-- et les dates suffisent ensuite à dire ce qui a été constaté.
SELECT
    'checklist (points enregistrés)'                                       AS donnee,
    count(*) FILTER (WHERE i."DatePrevue" < now() - interval '1 year')     AS hors_delai,
    count(*) FILTER (WHERE i."DatePrevue" < now() - interval '1 year')     AS supprimable_maintenant
FROM "ControlesEffectues" ce
JOIN "Interventions" i ON i."Id" = ce."InterventionId"

UNION ALL

SELECT
    'anomalies',
    count(*) FILTER (WHERE "DateSignalement" < now() - interval '1 year'),
    count(*) FILTER (WHERE "DateSignalement" < now() - interval '1 year' AND "Statut" = 'Traitee')
FROM "Anomalies"

UNION ALL

SELECT
    'photos d''anomalie',
    count(*) FILTER (WHERE a."Statut" = 'Traitee' AND a."DateTraitement" < now() - interval '3 months'),
    count(*) FILTER (WHERE a."Statut" = 'Traitee' AND a."DateTraitement" < now() - interval '3 months')
FROM "PhotosAnomalie" ph
JOIN "Anomalies" a ON a."Id" = ph."AnomalieId";

SELECT
    count(*) FILTER (WHERE "Statut" = 'Ouverte' AND "DateSignalement" < now() - interval '1 year')
                                                    AS ouvertes_hors_delai,
    min("DateSignalement") FILTER (WHERE "Statut" = 'Ouverte')
                                                    AS plus_ancienne_ouverte
FROM "Anomalies";

-- ---------------------------------------------------------------------------
-- La purge, commentée. L'ORDRE EST LA MOITIÉ DU TRAVAIL.
-- ---------------------------------------------------------------------------
--
-- Six clés étrangères de la chaîne d'exploitation sont en `Restrict` depuis la
-- migration `SuppressionRestreinteSurLaChaineExploitation`, et deux de plus le sont
-- sur l'agent — `Intervention.AgentId` et `Remplacement.AgentRemplacantId` —, et trois
-- sur la vacation depuis l'étape 25 : ses pointages, sa checklist, ses anomalies. Un
-- DELETE dans le mauvais ordre échoue sur une violation de clé, à mi-parcours, après
-- avoir déjà supprimé ce qui précédait. Lire l'ordre ci-dessous comme une contrainte,
-- pas comme une préférence de présentation.
--
-- Deux exceptions en `Cascade`. `Remplacement` : supprimer une intervention ou une
-- absence emporte ses remplacements, et le DELETE 1 ne sert donc qu'à purger ceux dont
-- le parent est encore dans son délai. Et `PhotoAnomalie` : une anomalie emporte ses
-- photos, qui n'existent que pour elle.
--
-- Chaque DELETE est atomique de lui-même. Aucune transaction englobante : elle
-- donnerait l'illusion qu'on peut défaire l'ensemble après coup, alors que le choix
-- utile est de rejouer le script.
--
-- -- 1. Remplacements (état dérivé)
-- DELETE FROM "Remplacements" WHERE "DateCreation" < now() - interval '1 year';
--
-- -- 1 bis. Photos d'anomalie, trois mois après le traitement (section 9)
-- DELETE FROM "PhotosAnomalie" ph
-- USING "Anomalies" a
-- WHERE a."Id" = ph."AnomalieId"
--   AND a."Statut" = 'Traitee' AND a."DateTraitement" < now() - interval '3 months';
--
-- -- 1 ter. Anomalies traitées, un an après le signalement (cascade sur leurs photos)
-- --    ⚠️ Jamais une anomalie ouverte : voir la section 9.
-- DELETE FROM "Anomalies"
-- WHERE "Statut" = 'Traitee' AND "DateSignalement" < now() - interval '1 year';
--
-- -- 1 quater. Checklist des vacations d'il y a plus d'un an
-- DELETE FROM "ControlesEffectues" ce
-- USING "Interventions" i
-- WHERE i."Id" = ce."InterventionId" AND i."DatePrevue" < now() - interval '1 year';
--
-- -- 2. Interventions (cascade sur leurs remplacements)
-- --    ⚠️ Les trois `NOT EXISTS` ne sont pas décoratifs : pointages, checklist et
-- --    anomalies tiennent à la vacation par `Restrict`, et un seul suffirait à faire
-- --    échouer tout le DELETE. Une vacation retenue attend `purge-pointages.sql` ou le
-- --    traitement de son anomalie — à rejouer, pas à forcer.
-- DELETE FROM "Interventions" i
-- WHERE i."DatePrevue" < now() - interval '5 years'
--   AND NOT EXISTS (SELECT 1 FROM "Pointages" po WHERE po."InterventionId" = i."Id")
--   AND NOT EXISTS (SELECT 1 FROM "ControlesEffectues" ce WHERE ce."InterventionId" = i."Id")
--   AND NOT EXISTS (SELECT 1 FROM "Anomalies" an WHERE an."InterventionId" = i."Id");
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
-- -- 6. Mouvements de stock (RGPD.md §5)
-- --    ⚠️ AVANT les sites : les quatre lieux d'un mouvement sont des clés en
-- --    `Restrict`, et un site cité par un mouvement n'est pas supprimable. Aucune clé
-- --    ne vise un mouvement, rien ne le bloque lui-même. Aucune quantité ne change.
-- DELETE FROM "MouvementsStock" WHERE "DateMouvement" < now() - interval '5 years';
--
-- -- 7. Sites sans contrat, créés il y a plus de 5 ans, sans stock ni mouvement rattaché
-- --    Couvre d'un même geste le site dont les contrats viennent d'être purgés par
-- --    le DELETE 5 et le prospect jamais signé. `DateCreation` les distingue d'un
-- --    site saisi hier — c'est tout l'objet de la colonne (RGPD.md §9.7).
-- DELETE FROM "Sites" s
-- WHERE s."DateCreation" < now() - interval '5 years'
--   AND NOT EXISTS (SELECT 1 FROM "Contrats" c WHERE c."SiteId" = s."Id")
--   AND NOT EXISTS (SELECT 1 FROM "Stocks" st WHERE st."SiteId" = s."Id")
--   AND NOT EXISTS (
--       SELECT 1 FROM "MouvementsStock" m
--       WHERE m."OrigineSiteId" = s."Id" OR m."DestinationSiteId" = s."Id");
--
-- -- 8. Clients sans site, créés il y a plus de 5 ans, sans compte rattaché
-- DELETE FROM "Clients" cl
-- WHERE cl."DateCreation" < now() - interval '5 years'
--   AND NOT EXISTS (SELECT 1 FROM "Sites" s WHERE s."ClientId" = cl."Id")
--   AND NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."ClientId" = cl."Id");
--
-- -- 9. Agents : AUCUN DELETE. Voir la section 6.
