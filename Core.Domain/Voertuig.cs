namespace Core.Domain;

public class Voertuig
{
    private int _kilometerstand;
    private decimal _afschrijvingPercent;
    private bool _beschikbaar;

    public int Id { get; }
    public VoertuigType Type { get; }
    public int Capaciteit { get; } // personen of kg, afhankelijk van type
    public int Kilometerstand => _kilometerstand;
    public decimal AfschrijvingPercent => _afschrijvingPercent;
    public bool Beschikbaar => _beschikbaar;

    public Voertuig(int id, VoertuigType type, int capaciteit, int kilometerstand, decimal afschrijvingPercent, bool beschikbaar)
    {
        if (capaciteit <= 0) throw new ArgumentOutOfRangeException(nameof(capaciteit));
        if (kilometerstand < 0) throw new ArgumentOutOfRangeException(nameof(kilometerstand));
        if (afschrijvingPercent < 0) throw new ArgumentOutOfRangeException(nameof(afschrijvingPercent));

        Id = id;
        Type = type;
        Capaciteit = capaciteit;
        _kilometerstand = kilometerstand;
        _afschrijvingPercent = afschrijvingPercent;
        _beschikbaar = beschikbaar;
    }

    public bool IsGeschikt(RitType ritType, int vereisteCapaciteit) =>
        (ritType == RitType.Personen && Type == VoertuigType.Personenauto && vereisteCapaciteit <= Capaciteit)
     || (ritType == RitType.Vracht && Type == VoertuigType.Vrachtauto && vereisteCapaciteit <= Capaciteit);

    public void Reserveer()
    {
        if (!_beschikbaar) throw new InvalidOperationException("Voertuig is niet beschikbaar.");
        _beschikbaar = false;
    }

    public void Vrijgeven() => _beschikbaar = true;

    public void RijdEnSchrijfAf(int geredenKm, int? beladingVoorVracht = null)
    {
        if (geredenKm <= 0) throw new ArgumentOutOfRangeException(nameof(geredenKm));
        _kilometerstand += geredenKm;

        if (Type == VoertuigType.Personenauto)
        {
            _afschrijvingPercent += (decimal)geredenKm / 2000m * 1.0m;
        }
        else
        {
            _afschrijvingPercent += (decimal)geredenKm / 3000m * 1.0m;
            if (beladingVoorVracht.HasValue && beladingVoorVracht.Value > (int)(Capaciteit * 0.9))
                _afschrijvingPercent += 0.5m;
        }
    }
}
