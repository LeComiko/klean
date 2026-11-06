-- Purge des pointages des passages (RGPD.md §5, traitement T7 ; SPEC.md étape 25).
--
-- Conservation : UN AN après le pointage.
--
-- Pourquoi un an. Un pointage ne garde aucune position — le serveur calcule la distance
-- au site, puis jette les coordonnées —, mais « à 140 m du site à 8 h 02 » situe encore
-- une personne à un instant : c'est une donnée dérivée d'une localisation. La CNIL
-- retient deux mois pour une géolocalisation de salarié, et un an quand elle sert à
-- prouver une prestation au client, ce qui est la finalité de T7. Sa norme NS-051 vise
-- les véhicules : l'appliquer à un téléphone est une analogie, à faire relire.
--
-- Ce qui disparaît, et ce qui reste. La purge emporte l'heure, la distance, la précision
-- et la preuve de chaque pointage. Le STATUT de la vacation — `Terminee` ou
-- `NonCertifiee` — reste sur `Interventions`, avec ses cinq ans : on saura encore que le
-- passage a été certifié, plus à quelle distance ni à quelle minute. C'est toute la
-- raison pour laquelle le pointage est une table à part et non des colonnes de la
-- vacation.
--
-- Pourquoi un script et non une tâche du serveur : même raison que
-- `purge-journal-acces.sql`. L'ordonnancement appartient à l'exploitation
-- (DEPLOIEMENT.md §7).
--
--     psql -d klean -f backend/Scripts/purge-pointages.sql                  # rapport seul
--     psql -d klean -v confirmer=1 -f backend/Scripts/purge-pointages.sql   # rapport, puis purge
--
-- Rejouable à l'identique : la seconde exécution ne trouve plus rien à purger. Aucune
-- clé ne vise un pointage, donc rien ne le bloque, et sa suppression ne bloque rien.
-- C'est aussi ce qu'il faut rejouer après la restauration d'une sauvegarde
-- (DEPLOIEMENT.md §4) : elle ressuscite les pointages purgés depuis.

\echo 'Pointages, par fenêtre et par preuve :'

SELECT
    CASE
        WHEN "Horodatage" < now() - interval '1 year' THEN 'au-delà d''un an — à purger'
        ELSE 'dans la fenêtre de conservation'
    END                             AS fenetre,
    "Preuve"                        AS preuve,
    count(*)                        AS pointages,
    count(DISTINCT "InterventionId") AS vacations,
    count(DISTINCT "AuteurId")      AS agents,
    min("Horodatage")               AS plus_ancien,
    max("Horodatage")               AS plus_recent
FROM "Pointages"
GROUP BY 1, 2
ORDER BY 1, 2;

\if :{?confirmer}
\else
    \echo ''
    \echo 'Rien n''a été supprimé. Pour purger les pointages de plus d''un an, relancer avec -v confirmer=1.'
    \quit
\endif

-- Un seul DELETE, donc atomique de lui-même. Aucune transaction englobante : elle
-- donnerait l'illusion qu'on peut défaire la purge après coup.
--
-- ⚠️ La borne est la date du POINTAGE, pas celle de la vacation : le début et la fin
-- d'un passage de nuit peuvent tomber à quelques heures d'écart de part et d'autre de la
-- borne, et l'exécution suivante emporte le second. Sans conséquence : aucun écran ne
-- montre les pointages d'une vacation d'il y a un an.
--
-- ⚠️ Ne jamais réécrire la condition en `now() - "Horodatage" > interval '1 year'`. Elle
-- a l'air équivalente et ne l'est pas : PostgreSQL compare deux intervalles en comptant
-- un mois pour trente jours, donc un an pour 360 jours, et cette forme purge un pointage
-- de 364 jours. `date < now() - interval` soustrait au calendrier. Constaté en éprouvant
-- ce script, sur la requête de contrôle et non sur le script.
DELETE FROM "Pointages"
WHERE "Horodatage" < now() - interval '1 year';
