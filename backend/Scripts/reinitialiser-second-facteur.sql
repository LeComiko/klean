-- Retrait du second facteur d'un compte, hors de l'application (SPEC.md, étape 23).
--
-- Le seul cas qui le justifie : le DERNIER administrateur d'une entreprise a perdu
-- son téléphone ET ses codes de récupération. Pour tout autre compte, un
-- administrateur passe par l'API — `POST /api/auth/comptes/{id}/reinitialiser-second-
-- facteur` —, qui vérifie l'entreprise et laisse une ligne au journal d'accès. Ce
-- script ne vérifie rien de tel, et ne laisse aucune ligne : la trace est celle que
-- l'exploitant garde de ses propres gestes.
--
-- Pourquoi aucune route de secours. Une route qui retire le second facteur sans code
-- est exactement ce qu'un attaquant muni du mot de passe chercherait. L'accès à la base
-- est la preuve qu'on est l'exploitant ; l'identité de la personne qui appelle se
-- vérifie AVANT, hors de KLEAN, et ce script ne peut pas le faire à votre place.
--
-- Deux temps, et le premier ne modifie rien :
--
--     psql -d klean -v email=nom@exemple.fr -f backend/Scripts/reinitialiser-second-facteur.sql
--     psql -d klean -v email=nom@exemple.fr -v confirmer=1 -f backend/Scripts/reinitialiser-second-facteur.sql
--
-- Les mêmes gestes que la route d'administration, et aucun de moins :
--
--   1. `TwoFactorEnabled` à faux ;
--   2. les trois jetons du fournisseur `[KleanSecondFacteur]` effacés — la clé
--      chiffrée, les empreintes des codes de récupération, le dernier pas accepté ;
--   3. `SecurityStamp` régénéré, ce qui périme les jetons d'accès en circulation ;
--   4. les sessions de rafraîchissement révoquées.
--
-- ⚠️ Le 3 et le 4 sont nécessaires tous les deux, pour la raison de CLAUDE.md §10 :
-- révoquer une session ne périme pas le jeton d'accès déjà émis, qui vaut encore
-- quinze minutes. Pour un rôle obligé, `OnTokenValidated` refuserait déjà ce jeton faute
-- de second facteur ; pour un compte où il est facultatif, seule l'empreinte le tue.
--
-- Ensuite : à sa connexion suivante, un compte au rôle obligé équipe un nouveau
-- téléphone dans la connexion même, et reçoit dix codes neufs. Rien d'autre à faire.

\set ON_ERROR_STOP on

\if :{?email}
\else
    \echo 'Adresse manquante : psql ... -v email=nom@exemple.fr -f backend/Scripts/reinitialiser-second-facteur.sql'
    \quit
\endif

-- La variable psql n'est pas interpolée dans le corps d'un bloc DO, qui est une chaîne :
-- elle passe par un réglage de session.
SELECT set_config('klean.email', :'email', false) AS compte_vise \gset

\echo ''
\echo 'Le compte visé — vérifier le nom, l''entreprise et le rôle avant de confirmer :'

-- Identity range l'adresse en majuscules dans `NormalizedEmail`, et l'unicité y porte.
SELECT
    u."Id",
    u."Prenom" || ' ' || u."Nom"                                     AS personne,
    e."RaisonSociale"                                                AS entreprise,
    (SELECT string_agg(r."Name", ', ')
       FROM "AspNetUserRoles" ur JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
      WHERE ur."UserId" = u."Id")                                    AS roles,
    u."Actif",
    u."TwoFactorEnabled"                                             AS second_facteur_actif,
    (SELECT count(*) FROM "AspNetUserTokens" t
      WHERE t."UserId" = u."Id" AND t."LoginProvider" = '[KleanSecondFacteur]') AS jetons_second_facteur,
    (SELECT count(*) FROM "JetonsRafraichissement" j
      WHERE j."UtilisateurId" = u."Id" AND j."DateRevocation" IS NULL
        AND j."DateExpiration" > now())                              AS sessions_ouvertes
FROM "AspNetUsers" u
JOIN "Entreprises" e ON e."Id" = u."EntrepriseId"
WHERE u."NormalizedEmail" = upper(current_setting('klean.email'));

\if :{?confirmer}
\else
    \echo ''
    \echo 'Rien n''a été modifié. Pour retirer le second facteur de ce compte, relancer avec -v confirmer=1.'
    \echo 'Aucune ligne ci-dessus : aucun compte ne porte cette adresse.'
    \quit
\endif

-- ⚠️ Un seul bloc DO, donc une seule instruction : les quatre gestes passent ensemble ou
-- pas du tout. Un compte au second facteur retiré mais aux sessions encore ouvertes
-- serait le pire des états intermédiaires. Et aucun BEGIN explicite, pour que le script
-- puisse s'éprouver dans une transaction qu'on annule.
DO $$
DECLARE
    cible     uuid;
    actif     boolean;
    jetons    integer;
    sessions  integer;
BEGIN
    SELECT "Id", "Actif" INTO STRICT cible, actif
    FROM "AspNetUsers"
    WHERE "NormalizedEmail" = upper(current_setting('klean.email'));

    UPDATE "AspNetUsers"
    SET "TwoFactorEnabled" = false,
        -- Le format d'Identity n'importe pas : l'empreinte est comparée telle quelle.
        "SecurityStamp"    = upper(replace(gen_random_uuid()::text, '-', '')),
        -- Le jeton de concurrence d'Identity : une écriture de l'API en cours sur ce
        -- compte échouera au lieu d'écraser ce retrait.
        "ConcurrencyStamp" = gen_random_uuid()::text
    WHERE "Id" = cible;

    DELETE FROM "AspNetUserTokens"
    WHERE "UserId" = cible AND "LoginProvider" = '[KleanSecondFacteur]';
    GET DIAGNOSTICS jetons = ROW_COUNT;

    UPDATE "JetonsRafraichissement"
    SET "DateRevocation"  = now(),
        "MotifRevocation" = 'Réinitialisation du second facteur par l''exploitant'
    WHERE "UtilisateurId" = cible AND "DateRevocation" IS NULL;
    GET DIAGNOSTICS sessions = ROW_COUNT;

    RAISE NOTICE 'Second facteur retiré : % jeton(s) effacé(s), % session(s) révoquée(s).', jetons, sessions;

    IF NOT actif THEN
        RAISE NOTICE 'Ce compte est DÉSACTIVÉ : il ne pourra pas se connecter pour autant.';
    END IF;
EXCEPTION
    WHEN no_data_found THEN
        RAISE EXCEPTION 'Aucun compte pour l''adresse « % ». Rien n''a été modifié.',
            current_setting('klean.email');
END
$$;
