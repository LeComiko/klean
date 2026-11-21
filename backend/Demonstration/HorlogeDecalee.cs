namespace Klean.Api.Demonstration;

/// <summary>
/// Une horloge posée à un autre jour, qui avance avec la vraie (SPEC.md, étape 28).
///
/// Le jeu de démonstration rejoue le passé par elle : la génération part de deux semaines
/// en arrière, et un pointage d'il y a trois jours passe par <c>ServicePassage</c> comme ce
/// jour-là. Aucune règle n'est contournée — le service refuse toujours une vacation qui
/// n'est pas « du jour », c'est le jour qui a changé.
/// </summary>
public sealed class HorlogeDecalee : TimeProvider
{
    private readonly TimeProvider _reelle;
    private readonly TimeSpan _decalage;

    public HorlogeDecalee(TimeProvider reelle, TimeSpan decalage)
    {
        _reelle = reelle;
        _decalage = decalage;
    }

    public override DateTimeOffset GetUtcNow() => _reelle.GetUtcNow() + _decalage;

    public override TimeZoneInfo LocalTimeZone => _reelle.LocalTimeZone;
}
