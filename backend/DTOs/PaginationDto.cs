namespace Klean.Api.DTOs;

/// <summary>
/// Paramètres de pagination, liés depuis la chaîne de requête. Les valeurs sont
/// bornées par les accesseurs eux-mêmes : sans plafond, un seul appel à
/// <c>GET /api/interventions</c> suffit à épuiser la mémoire du serveur.
/// </summary>
public class PaginationDto
{
    public const int TailleParDefaut = 50;
    public const int TailleMaximale = 200;

    private int _page = 1;
    private int _taille = TailleParDefaut;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int Taille
    {
        get => _taille;
        set => _taille = value < 1 ? TailleParDefaut : Math.Min(value, TailleMaximale);
    }

    /// <summary>Nombre de lignes à sauter, pour <c>Skip</c>.</summary>
    public int ANegliger => (Page - 1) * Taille;
}

/// <summary>
/// Réponse enveloppée. <c>Total</c> est le nombre de lignes visibles par
/// l'appelant après application des filtres, pas le nombre de lignes de la table.
/// </summary>
public class PageDto<T>
{
    public IEnumerable<T> Elements { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int Taille { get; set; }
}
