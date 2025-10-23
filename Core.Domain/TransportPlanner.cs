namespace Core.Domain;

public class RitAanvraag
{
    public int KlantId { get; init; }
    public DateTime Datum { get; init; }
    public RitType Type { get; init; }
    public int AfstandKm { get; init; }
    public int? AantalPersonen { get; init; }
    public int? GewichtKg { get; init; }
}