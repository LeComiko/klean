-- Purge des jetons de rafraîchissement morts (RGPD.md §9.2).
--
-- Conservation : TRENTE JOURS après révocation ou expiration.
--
-- Pourquoi ce script existe. `JetonsRafraichissement` porte la seule adresse IP du
-- produit — `AdresseIp`, renseignée à chaque ouverture de session ET à chaque
-- rotation, donc plusieurs fois par jour et par utilisateur — et rien ne supprimait
-- jamais ces lignes : ni l'expiration, ni la révocation, ni la déconnexion. Elles
-- s'accumulaient indéfiniment avec leur adresse.
--
-- L'ironie était à nommer, et RGPD.md §9.2 la nomme : le journal d'accès de l'étape 17
-- refuse délibérément de stocker une adresse IP — « une donnée personnelle de plus,
-- dont la conservation demanderait sa propre justification » — pendant que la table de
-- sessions en gardait l'historique complet.
--
-- La colonne a bien un usage : diagnostiquer un rejeu. Mais cet usage dure des jours,
-- pas des années. Trente jours couvrent largement le délai entre une alerte et son
-- examen, et bornent la conservation à ce que l'usage justifie.
--
-- Pourquoi un script d'exploitation et non une tâche du serveur. Même raison que pour
-- `purge-journal-acces.sql` : une tâche planifiée embarquée existerait en un seul
-- exemplaire logique alors que le produit peut tourner en plusieurs instances.
-- L'ordonnancement appartient à l'exploitation (SPEC.md étape 18).
--
--     psql -d klean -f backend/Scripts/purge-jetons-rafraichissement.sql
--
-- ⚠️ Lire d'abord, supprimer ensuite. Le SELECT ne modifie rien ; le DELETE est
-- commenté exprès.

\echo 'Jetons de rafraîchissement, par état :'

-- ⚠️ Un jeton VIVANT n'est jamais dans la fenêtre de purge, et c'est la seule
-- propriété à ne pas casser en modifiant ce script : il se reconnaît à
-- « DateRevocation IS NULL ET DateExpiration dans le futur ». Les deux branches du
-- CASE ci-dessous l'excluent toutes les deux, et l'ordre des branches compte — un
-- jeton révoqué dont l'expiration est aussi passée doit compter une fois.
SELECT
    CASE
        WHEN "DateRevocation" IS NULL AND "DateExpiration" > now()
            THEN '1 — vivant, à ne jamais purger'
        WHEN ("DateRevocation" IS NOT NULL AND "DateRevocation" >= now() - interval '30 days')
          OR ("DateRevocation" IS NULL  AND "DateExpiration" >= now() - interval '30 days')
            THEN '2 — mort, dans la fenêtre de diagnostic'
        ELSE '3 — mort depuis plus de 30 jours — à purger'
    END                                                AS etat,
    count(*)                                           AS jetons,
    count("AdresseIp")                                 AS dont_avec_adresse_ip,
    count(DISTINCT "UtilisateurId")                    AS comptes_concernes,
    min(coalesce("DateRevocation", "DateExpiration"))  AS plus_ancien,
    max(coalesce("DateRevocation", "DateExpiration"))  AS plus_recent
FROM "JetonsRafraichissement"
GROUP BY 1
ORDER BY 1;

\echo ''
\echo 'Motifs de révocation des lignes à purger — ce que la purge fait disparaître :'

SELECT
    coalesce("MotifRevocation", '(expiré sans révocation)') AS motif,
    count(*)                                               AS jetons
FROM "JetonsRafraichissement"
WHERE NOT ("DateRevocation" IS NULL AND "DateExpiration" > now())
  AND coalesce("DateRevocation", "DateExpiration") < now() - interval '30 days'
GROUP BY 1
ORDER BY 2 DESC;

-- Décommenter pour purger. Aucune transaction explicite : un DELETE est déjà
-- atomique, et l'envelopper donnerait l'illusion qu'on peut le défaire après coup.
--
-- ⚠️ `RemplacePar` pointe vers le jeton émis en rotation, SANS clé étrangère — c'est
-- une colonne `uuid` nue, vérifiable dans `KleanDbContext`. La purge peut donc laisser
-- des pointeurs vers des lignes disparues, et c'est sans conséquence : la chaîne d'une
-- session ne sert qu'au diagnostic d'un rejeu, dans la fenêtre que ce script préserve.
-- Au-delà, il n'y a plus de chaîne à suivre.
--
-- ⚠️ La première condition est ce qui protège les sessions ouvertes. Ne jamais la
-- retirer « parce que la seconde suffit » : un jeton vivant dont l'expiration est
-- lointaine passe la seconde condition dès que `DateRevocation` est nulle.
--
-- DELETE FROM "JetonsRafraichissement"
-- WHERE NOT ("DateRevocation" IS NULL AND "DateExpiration" > now())
--   AND coalesce("DateRevocation", "DateExpiration") < now() - interval '30 days';
