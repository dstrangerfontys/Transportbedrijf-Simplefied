using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    /// De TransportPlanner is verantwoordelijk voor het plannen en afronden van ritten.
    /// Hij vormt de schakel tussen de domeinlaag (Core.Domain) en de database (Infrastructure).
    public class TransportPlanner
    {
        private readonly MySqlConnectionFactory _factory;   // Maakt connecties aan met de database
        private readonly VoertuigRepository _voertuigen;    // Behandelt database-operaties voor voertuigen
        private readonly RitRepository _ritten;             // Behandelt database-operaties voor ritten

        public TransportPlanner(MySqlConnectionFactory factory, VoertuigRepository vRepo, RitRepository rRepo)
        {
            _factory = factory;
            _voertuigen = vRepo;
            _ritten = rRepo;
        }

        /// Plant een nieuwe rit op basis van een rit-aanvraag.
        /// 1. Zoekt een geschikt voertuig
        /// 2. Reserveert dit voertuig
        /// 3. Maakt een nieuwe rit aan
        /// 4. Slaat de rit op in de database
        public async Task<Rit?> PlanAsync(RitAanvraag aanvraag)
        {
            // Maak een nieuwe databaseconnectie aan
            using var conn = _factory.Create();
            await conn.OpenAsync();

            // Start een database-transactie (zodat alles of niets wordt opgeslagen)
            using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted) as MySqlTransaction;

            try
            {
                // Bepaal wat "capaciteit" betekent:
                // - Bij personenritten is dat aantal personen
                // - Bij vrachtritten is dat het gewicht
                int cap = aanvraag.Type == RitType.Personen
                    ? (aanvraag.AantalPersonen ?? 0)
                    : (aanvraag.GewichtKg ?? 0);

                // Vraag geschikte, beschikbare voertuigen op
                var kandidaten = await _voertuigen.GetGeschiktEnBeschikbaarAsync(conn, tx!, aanvraag.Type, cap);
                var voertuig = kandidaten.FirstOrDefault();

                // Geen voertuig beschikbaar? => Transactie rollbacken en null teruggeven
                if (voertuig is null)
                {
                    await tx!.RollbackAsync();
                    return null;
                }

                // Reserveer het voertuig (zet beschikbaarheid op false)
                voertuig.Reserveer();
                await _voertuigen.UpdateStateAsync(conn, tx!, voertuig);

                // Maak een nieuwe rit aan met status "Gepland"
                var rit = new Rit(
                    id: 0,
                    klantId: aanvraag.KlantId,
                    voertuigId: voertuig.Id,
                    datum: aanvraag.Datum,
                    type: aanvraag.Type,
                    afstandKm: aanvraag.AfstandKm,
                    aantalPersonen: aanvraag.AantalPersonen,
                    gewichtKg: aanvraag.GewichtKg,
                    status: "Gepland" // <-- belangrijk, voorkomt 'None'-fouten
                );

                // Bereken prijs op basis van type en afstand
                rit.BerekenPrijs();

                // Voeg rit toe aan de database en ontvang nieuw ID
                var newId = await _ritten.AddAsync(conn, tx!, rit);

                // Maak nieuwe instantie met het ID dat de database heeft gegenereerd
                rit = new Rit(
                    id: newId,
                    klantId: rit.KlantId,
                    voertuigId: rit.VoertuigId,
                    datum: rit.Datum,
                    type: rit.Type,
                    afstandKm: rit.AfstandKm,
                    aantalPersonen: rit.AantalPersonen,
                    gewichtKg: rit.GewichtKg,
                    status: "Gepland",
                    prijs: rit.Prijs
                );

                // Transactie committen: alle wijzigingen definitief opslaan
                await tx!.CommitAsync();

                // Rit teruggeven aan de UI-laag
                return rit;
            }
            catch
            {
                // Iets ging mis → alle wijzigingen terugdraaien
                await tx!.RollbackAsync();
                throw;
            }
        }

        /// Rondt een rit af:
        /// 1. Laadt de rit en het voertuig
        /// 2. Werkt kilometerstand & afschrijving bij
        /// 3. Zet status van rit op "Afgerond"
        public async Task VoltooiRitAsync(int ritId, int geredenKm)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync() as MySqlTransaction;

            try
            {
                // Haal rit op uit de database
                var rit = await _ritten.GetByIdAsync(conn, tx, ritId);
                if (rit is null)
                {
                    await tx!.RollbackAsync();
                    return;
                }

                // Vergrendel het voertuig zodat geen andere transactie het tegelijk kan aanpassen
                using (var lockCmd = new MySqlCommand("SELECT 1 FROM voertuig WHERE voertuigID=@id FOR UPDATE;", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("@id", rit.VoertuigId);
                    await lockCmd.ExecuteNonQueryAsync();
                }

                // Laad voertuiggegevens
                Voertuig? v = null;
                using (var cmd = new MySqlCommand("SELECT * FROM voertuig WHERE voertuigID=@id;", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", rit.VoertuigId);
                    using var rdr = await cmd.ExecuteReaderAsync();

                    if (await rdr.ReadAsync())
                    {
                        // Ordinals verbeteren performance (sneller dan strings)
                        int oVoertuigId = rdr.GetOrdinal("voertuigID");
                        int oType = rdr.GetOrdinal("type");
                        int oCapaciteit = rdr.GetOrdinal("capaciteit");
                        int oKilometer = rdr.GetOrdinal("kilometerstand");
                        int oAfschrijving = rdr.GetOrdinal("afschrijving");
                        int oBeschikbaar = rdr.GetOrdinal("beschikbaar");

                        // Zet type-tekst om naar enum
                        var typeStr = rdr.IsDBNull(oType) ? "" : rdr.GetString(oType);
                        var type = typeStr == "Personenauto" ? VoertuigType.Personenauto : VoertuigType.Vrachtauto;

                        // Maak voertuigobject
                        v = new Voertuig(
                            rdr.GetInt32(oVoertuigId),
                            type,
                            rdr.GetInt32(oCapaciteit),
                            rdr.GetInt32(oKilometer),
                            rdr.GetDecimal(oAfschrijving),
                            rdr.GetBoolean(oBeschikbaar)
                        );
                    }
                }

                // Geen voertuig gevonden? -> transactie terugdraaien
                if (v is null)
                {
                    await tx!.RollbackAsync();
                    return;
                }

                // Bereken afschrijving en kilometerstand op basis van gereden km
                int? belading = rit.Type == RitType.Vracht ? rit.GewichtKg : null;
                v.RijdEnSchrijfAf(geredenKm, belading);
                v.Vrijgeven(); // voertuig weer beschikbaar maken
                await _voertuigen.UpdateStateAsync(conn, tx!, v);

                // Werk ritstatus bij naar "Afgerond"
                rit.Afronden();
                await _ritten.UpdateAsync(conn, tx!, rit);

                // Alle wijzigingen committen
                await tx!.CommitAsync();
            }
            catch
            {
                // Iets fout gegaan -> rollback
                await tx!.RollbackAsync();
                throw;
            }
        }
    }
}