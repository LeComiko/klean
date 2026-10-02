-- Dégradation des absences de type Maladie (RGPD.md §8.2).
--
-- Conservation de la QUALIFICATION de santé : TROIS MOIS après la date de fin.
--
-- Ce que ce script fait, et pourquoi ce n'est pas une suppression. Un arrêt maladie ne
-- porte jamais de motif — `AbsencesController` le refuse en 400 depuis l'étape 8 — mais
-- l'absence garde son `Type` et ses dates, et « Maladie » est une donnée de santé au
-- sens de l'article 9 du RGPD. Supprimer la ligne casserait l'historique des
-- remplacements qui en découlent : `Remplacement.AbsenceId` est en `Cascade`, donc la
-- suppression emporterait silencieusement les remplacements déclenchés par cette
-- absence.
--
-- Or ce dont l'historique des remplacements a besoin, c'est **qu'il y avait un trou**,
-- pas pourquoi. Le `Type` bascule donc vers `Indisponibilite` — valeur qui existe déjà
-- dans l'énumération `TypeAbsence` — et l'agent, les dates et la chaîne de
-- remplacements restent intacts. La donnée de santé disparaît ; l'exploitation ne perd
-- rien.
--
-- Pourquoi trois mois. KLEAN met la paie hors périmètre (`CLAUDE.md` §1) : il n'a donc
-- aucune justification pour conserver la qualification d'un arrêt au-delà du
-- remplacement et de la contestation du mois concerné. Ce sont le logiciel de paie et
-- le dossier du personnel, pas celui-ci, qui portent les obligations longues sur les
-- arrêts de travail.
--
--     psql -d klean -f backend/Scripts/degradation-absences-maladie.sql
--
-- ⚠️ Deux réserves, écrites parce qu'elles ne doivent pas être découvertes après coup.
--
--   1. L'opération est IRRÉVERSIBLE et porte sur des lignes métier. Rien ne permet de
--      retrouver qu'une indisponibilité était un arrêt maladie — c'est précisément
--      l'objectif. Elle demande donc le même soin qu'une migration : sauvegarde
--      d'abord.
--   2. Elle supprime la QUALIFICATION, pas la possibilité d'une inférence. Une
--      indisponibilité de trois semaines reste une indisponibilité de trois semaines.
--      Ce que l'article 9 protège est la qualification ; c'est ce que ce script retire.
--
-- ⚠️ Et une vérification à ne pas sauter : rien dans le code ne branche sur
-- `Type = Maladie` en lecture. Les filtres durs du moteur d'affectation raisonnent sur
-- les DATES (`CLAUDE.md` §8), `DetecterImpactsAsync` traite tous les types, et le seul
-- endroit qui nomme `Maladie` est le refus de motif à l'écriture. La dégradation ne
-- change donc aucun comportement. Si un futur traitement venait à distinguer les
-- arrêts maladie, cette phrase devient fausse.

\echo 'Absences de type Maladie, par fenêtre :'

SELECT
    CASE
        WHEN "DateFin" >= now() - interval '3 months'
            THEN '1 — dans la fenêtre, à ne pas toucher'
        ELSE '2 — au-delà de 3 mois — à dégrader'
    END                   AS fenetre,
    count(*)              AS absences,
    count("Motif")        AS dont_avec_motif,
    min("DateFin")        AS plus_ancienne,
    max("DateFin")        AS plus_recente
FROM "Absences"
WHERE "Type" = 'Maladie'
GROUP BY 1
ORDER BY 1;

\echo ''
\echo 'Remplacements rattachés aux absences à dégrader — ce que la dégradation PRÉSERVE :'

-- Ce compte est la démonstration de l'intérêt du procédé. Ces lignes seraient perdues
-- par un DELETE, et elles survivent à l'UPDATE.
SELECT count(*) AS remplacements_preserves
FROM "Remplacements" r
JOIN "Absences" a ON a."Id" = r."AbsenceId"
WHERE a."Type" = 'Maladie'
  AND a."DateFin" < now() - interval '3 months';

\echo ''
\echo 'Anomalie à signaler : un arrêt maladie porteur d''un motif'

-- Ne devrait jamais arriver — `AbsencesController` refuse en 400 depuis l'étape 8. Une
-- ligne ici signale soit une donnée antérieure à cette règle, soit une écriture qui a
-- contourné l'API. Le motif est une donnée de santé en texte libre : à traiter avant
-- tout le reste.
SELECT count(*) AS arrets_maladie_avec_motif
FROM "Absences"
WHERE "Type" = 'Maladie' AND "Motif" IS NOT NULL;

-- Décommenter pour dégrader. Aucune transaction explicite : un UPDATE est déjà
-- atomique, et l'envelopper donnerait l'illusion qu'on peut le défaire après coup —
-- ce qui est faux, et dangereusement rassurant sur une opération irréversible.
--
-- ⚠️ `Indisponibilite` et non `Autre` : l'énumération `TypeAbsence` porte les deux, et
-- « indisponibilité » décrit exactement ce qui reste connu — l'agent n'était pas
-- disponible, à ces dates. « Autre » dirait qu'on ne sait pas de quoi il s'agissait,
-- ce qui est une information de moins et non une information neutre.
--
-- UPDATE "Absences"
-- SET "Type" = 'Indisponibilite', "Motif" = NULL
-- WHERE "Type" = 'Maladie'
--   AND "DateFin" < now() - interval '3 months';
