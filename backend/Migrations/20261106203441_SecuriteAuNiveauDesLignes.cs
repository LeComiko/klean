using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Klean.Api.Migrations
{
    /// <summary>
    /// La sécurité au niveau des lignes sur l'axe de l'entreprise (SPEC.md, étape 26).
    ///
    /// Aucune table, aucune colonne, aucune donnée ne change : le modèle d'EF est intact, et
    /// tout est en SQL. La migration crée <c>klean_app</c>, lui donne la lecture et l'écriture
    /// des lignes, et pose une politique par table qui porte <c>EntrepriseId</c>.
    /// <c>BasculeRoleApplicatif</c> met ensuite chaque connexion de l'API sous ce rôle.
    ///
    /// ⚠️ Pas de <c>FORCE ROW LEVEL SECURITY</c> : le propriétaire, qui joue les migrations et
    /// les scripts de conservation, n'est pas soumis. C'est le contournement voulu, et le seul.
    /// </summary>
    public partial class SecuriteAuNiveauDesLignes : Migration
    {
        /// <summary>
        /// Les tables qui portent <c>EntrepriseId</c>, sauf <c>AspNetUsers</c> : Identity la lit
        /// hors de toute requête authentifiée, et elle n'est pas filtrée (CLAUDE.md §7).
        /// <c>SecuriteDesLignesTests</c> compare cette liste au catalogue de la base.
        /// </summary>
        private static readonly string[] Tables =
        [
            "Absences", "AccesJournalises", "Agents", "Anomalies", "Clients", "ConsommationsType",
            "Contrats", "ControlesEffectues", "Emplacements", "Interventions", "MouvementsStock",
            "PhotosAnomalie", "Pointages", "PointsControle", "Prestations", "Produits",
            "Remplacements", "Sites", "Stocks", "TypesPrestation"
        ];

        /// <summary>
        /// ⚠️ <c>nullif</c> n'est pas décoratif : convertir une chaîne vide en <c>uuid</c> lève une
        /// erreur. Sans entreprise, le réglage est vide, et la comparaison doit donner « aucune
        /// ligne », pas une panne. Le second argument de <c>current_setting</c> fait de même
        /// quand le réglage n'a jamais été posé.
        /// </summary>
        private const string EntrepriseDuReglage =
            "nullif(current_setting('klean.entreprise', true), '')::uuid";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Un rôle vit dans la grappe, pas dans la base : `klean` et `klean_test` en partagent
            // une en développement, et la seconde migration rencontre le rôle de la première.
            // Sans connexion, sans droit de contourner la RLS, sans héritage de privilèges.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'klean_app') THEN
                        CREATE ROLE klean_app NOLOGIN NOSUPERUSER NOBYPASSRLS NOINHERIT;
                    END IF;
                END $$;
                """);

            // `SET ROLE` exige d'être membre du rôle, sauf pour un superutilisateur. Le compte
            // principal d'une base gérée ne l'est pas : sans cette ligne, l'API y échouerait à
            // sa première connexion.
            migrationBuilder.Sql("GRANT klean_app TO CURRENT_USER;");

            // Lire et écrire les lignes, rien d'autre : ni structure, ni `TRUNCATE`. Les
            // privilèges par défaut donnent les mêmes droits aux tables des migrations futures,
            // créées par le même propriétaire. L'historique des migrations n'est pas à l'API.
            migrationBuilder.Sql("""
                GRANT USAGE ON SCHEMA public TO klean_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO klean_app;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO klean_app;
                REVOKE ALL ON "__EFMigrationsHistory" FROM klean_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO klean_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    GRANT USAGE, SELECT ON SEQUENCES TO klean_app;
                """);

            // En lecture comme en écriture : `WITH CHECK` refuse une ligne posée ou déplacée
            // vers une autre entreprise, sous le forçage d'`EntrepriseId` du `DbContext`.
            foreach (var table in Tables)
            {
                Politique(migrationBuilder, table, "EntrepriseId");
            }

            // L'entreprise elle-même, sur son propre identifiant — comme son filtre EF, et pour
            // la même raison : elle ne porte pas de colonne `EntrepriseId`.
            Politique(migrationBuilder, "Entreprises", "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables.Append("Entreprises"))
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS entreprise ON "{table}";
                    ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }

            // ⚠️ Le rôle reste : il vit dans la grappe, et une autre base peut s'en servir. Ses
            // droits sur celle-ci partent.
            migrationBuilder.Sql("""
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM klean_app;
                ALTER DEFAULT PRIVILEGES IN SCHEMA public
                    REVOKE USAGE, SELECT ON SEQUENCES FROM klean_app;
                REVOKE ALL ON ALL TABLES IN SCHEMA public FROM klean_app;
                REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM klean_app;
                REVOKE USAGE ON SCHEMA public FROM klean_app;
                """);
        }

        private static void Politique(MigrationBuilder migrationBuilder, string table, string colonne)
        {
            migrationBuilder.Sql($"""
                ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                CREATE POLICY entreprise ON "{table}"
                    USING ("{colonne}" = {EntrepriseDuReglage})
                    WITH CHECK ("{colonne}" = {EntrepriseDuReglage});
                """);
        }
    }
}
