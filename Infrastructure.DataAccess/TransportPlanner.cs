using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    /// De TransportPlanner is verantwoordelijk voor het plannen en afronden van ritten.
    /// Dit vormt de schakel tussen de domeinlaag (Core.Domain) en de database (Infrastructure).
    public class TransportPlanner
    {
        private readonly MySqlConnectionFactory _factory;   // Maakt DB-connecties
        private readonly VoertuigRepository _voertuigen;    // DB-operaties voor voertuigen
        private readonly RitRepository _ritten;             // DB-operaties voor ritten

        public TransportPlanner(MySqlConnectionFactory factory, VoertuigRepository vRepo, RitRepository rRepo)
        {
            _factory = factory;
            _voertuigen = vRepo;
            _ritten = rRepo;
        }

        /// Plant een nieuwe rit op basis van een rit-aanvraag.
        /// 1 Zoek een geschikt en beschikbaar voertuig
        /// 2 Reserveer voertuig (beschikbaar -> false)
        /// 3 Maak rit aan (status "Gepland"), bereken prijs
        /// 4 Sla op en commit
        /// 
        /// Als er géén voertuig is:
        /// - log een rij met status "Afgewezen"
        public async Task<Rit?> PlanAsync(RitAanvraag aanvraag)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();

            // Gebruik transactie zodat selectie en reservering consistent gebeuren
            using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted) as MySqlTransaction;

            try
            {
                // Vereiste capaciteit: bij Personen = aantalPersonen, bij Vracht = gewicht (kg)
                int cap = aanvraag.Type == RitType.Personen
                    ? (aanvraag.AantalPersonen ?? 0)
                    : (aanvraag.GewichtKg ?? 0);

                // Kandidaten zoeken (rijen worden vergrendeld met FOR UPDATE in de repo)
                var kandidaten = await _voertuigen.GetGeschiktEnBeschikbaarAsync(conn, tx!, aanvraag.Type, cap);
                var voertuig = kandidaten.FirstOrDefault();

                // === GEEN VOERTUIG → log "Afgewezen" en commit ===
                if (voertuig is null)
                {
                    await _ritten.AddRejectedAsync(
                        conn, tx!,
                        klantId: aanvraag.KlantId,
                        datum: aanvraag.Datum,
                        type: aanvraag.Type,
                        afstandKm: aanvraag.AfstandKm,
                        aantalPersonen: aanvraag.AantalPersonen,
                        gewichtKg: aanvraag.GewichtKg
                    );

                    await tx!.CommitAsync();   // afwijzing definitief opslaan
                    return null;               // UI toont dan "aanvraag afgewezen"
                }

                // === Wel voertuig → reserveren en rit plannen ===
                voertuig.Reserveer();
                await _voertuigen.UpdateStateAsync(conn, tx!, voertuig);

                var rit = new Rit(
                    id: 0,
                    klantId: aanvraag.KlantId,
                    voertuigId: voertuig.Id,
                    datum: aanvraag.Datum,
                    type: aanvraag.Type,
                    afstandKm: aanvraag.AfstandKm,
                    aantalPersonen: aanvraag.AantalPersonen,
                    gewichtKg: aanvraag.GewichtKg,
                    status: "Gepland"
                );

                rit.BerekenPrijs(); // prijs: Personen = km * 1.0m, Vracht = km * 2.0m

                var newId = await _ritten.AddAsync(conn, tx!, rit);
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

                await tx!.CommitAsync();
                return rit;
            }
            catch
            {
                await tx!.RollbackAsync();
                throw;
            }
        }

        /// Rondt een rit af:
        /// 1. Laadt de rit en vergrendelt het voertuig
        /// 2. Werkt kilometerstand & afschrijving bij (incl. belading-regel 90% voor vracht)
        /// 3. Zet status van rit op "Afgerond"
        public async Task VoltooiRitAsync(int ritId, int geredenKm)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync() as MySqlTransaction;

            try
            {
                // Rit ophalen
                var rit = await _ritten.GetByIdAsync(conn, tx, ritId);
                if (rit is null)
                {
                    await tx!.RollbackAsync();
                    return;
                }

                // Vergrendel voertuig-rij om race conditions te voorkomen
                using (var lockCmd = new MySqlCommand("SELECT 1 FROM voertuig WHERE voertuigID=@id FOR UPDATE;", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("@id", rit.VoertuigId);
                    await lockCmd.ExecuteNonQueryAsync();
                }

                // Voertuig laden
                Voertuig? v = null;
                using (var cmd = new MySqlCommand("SELECT * FROM voertuig WHERE voertuigID=@id;", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", rit.VoertuigId);
                    using var rdr = await cmd.ExecuteReaderAsync();

                    if (await rdr.ReadAsync())
                    {
                        int oVoertuigId = rdr.GetOrdinal("voertuigID");
                        int oType = rdr.GetOrdinal("type");
                        int oCapaciteit = rdr.GetOrdinal("capaciteit");
                        int oKilometer = rdr.GetOrdinal("kilometerstand");
                        int oAfschrijving = rdr.GetOrdinal("afschrijving");
                        int oBeschikbaar = rdr.GetOrdinal("beschikbaar");

                        var typeStr = rdr.IsDBNull(oType) ? "" : rdr.GetString(oType);
                        var type = typeStr == "Personenauto" ? VoertuigType.Personenauto : VoertuigType.Vrachtauto;

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

                if (v is null)
                {
                    await tx!.RollbackAsync();
                    return;
                }

                // Afschrijving & km bijwerken
                int? belading = rit.Type == RitType.Vracht ? rit.GewichtKg : null;
                v.RijdEnSchrijfAf(geredenKm, belading);

                // Voertuig vrijgeven en opslaan
                v.Vrijgeven();
                await _voertuigen.UpdateStateAsync(conn, tx!, v);

                // Rit-status bijwerken
                rit.Afronden();
                await _ritten.UpdateAsync(conn, tx!, rit);

                await tx!.CommitAsync();
            }
            catch
            {
                await tx!.RollbackAsync();
                throw;
            }
        }
    }
}