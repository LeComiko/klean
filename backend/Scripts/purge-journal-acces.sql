-- Purge du journal d'accès aux données personnelles (SPEC.md étape 17).
--
-- Conservation : DOUZE MOIS. Un journal RGPD est lui-même une donnée personnelle —
-- il dit qui a travaillé, quand, et sur quoi. Le garder sans limite contredirait ce
-- qu'il sert, et ferait de la minimisation un principe appliqué partout sauf à
-- l'outil chargé d'en rendre compte.
--
-- Pourquoi un script d'exploitation et non un endpoint. Un journal que l'application
-- peut élaguer ne prouve rien : il suffirait d'un compte administrateur compromis
-- pour effacer la trace de ce qu'il a lu. La purge est donc un geste sur la base,
-- délibérément hors de portée de l'API — `JournalAccesController` n'expose aucun
-- DELETE, et ce n'est pas un oubli.
--
-- Pourquoi pas de tâche planifiée dans le serveur. Elle existerait en un seul
-- exemplaire logique alors que le produit peut tourner en plusieurs instances, et
-- une purge jouée deux fois en parallèle sur la même fenêtre n'est pas plus juste
-- qu'une purge jamais jouée. L'ordonnancement appartient à l'exploitation
-- (étape 18).
--
--     psql -d klean -f backend/Scripts/purge-journal-acces.sql
--
-- ⚠️ Lire d'abord, supprimer ensuite. Le SELECT ci-dessous ne modifie rien et donne
-- le volume concerné ; le DELETE est commenté exprès.

\echo 'Lignes du journal, par ancienneté :'

SELECT
    CASE
        WHEN "Horodatage" < now() - interval '12 months' THEN 'au-delà de 12 mois — à purger'
        ELSE 'dans la fenêtre de conservation'
    END                        AS fenetre,
    count(*)                   AS lignes,
    min("Horodatage")          AS plus_ancienne,
    max("Horodatage")          AS plus_recente
FROM "AccesJournalises"
GROUP BY 1
ORDER BY 1;

-- Décommenter pour purger. Aucune transaction explicite : un DELETE est déjà
-- atomique, et l'envelopper donnerait l'illusion qu'on peut le défaire après coup.
--
-- DELETE FROM "AccesJournalises"
-- WHERE "Horodatage" < now() - interval '12 months';
