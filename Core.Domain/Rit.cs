namespace Core.Domain;

public class Rit
{
    private string _status; // Gepland / Afgewezen / Afgerond
    private decimal _prijs;

    public int Id { get; }
    public int KlantId { get; }
    public int VoertuigId { get; }
    public DateTime Datum { get; }
    public RitType Type { get; }
    public int AfstandKm { get; }
    public int? AantalPersonen { get; }
    public int? GewichtKg { get; }
    public decimal Prijs => _prijs;
    public string Status => _status;

    public Rit(int id, int klantId, int voertuigId, DateTime datum, RitType type, int afstandKm,
               int? aantalPersonen = null, int? gewichtKg = null, string status = "Gepland", decimal prijs = 0m)
    {
        if (afstandKm <= 0) throw new ArgumentOutOfRangeException(nameof(afstandKm));
        Id = id;
        KlantId = klantId;
        VoertuigId = voertuigId;
        Datum = datum.Date;
        Type = type;
        AfstandKm = afstandKm;
        AantalPersonen = aantalPersonen;
        GewichtKg = gewichtKg;
        _status = status;
        _prijs = prijs;
    }

    public void BerekenPrijs()
    {
        _prijs = Type == RitType.Personen ? AfstandKm * 1.0m : AfstandKm * 2.0m;
    }

    public void Afwijzen()
    {
        if (_status != "Gepland") throw new InvalidOperationException("Alleen geplande ritten kunnen worden afgewezen.");
        _status = "Afgewezen";
    }

    public void Afronden()
    {
        if (_status != "Gepland") throw new InvalidOperationException("Alleen geplande ritten kunnen worden afgerond.");
        _status = "Afgerond";
    }
}
