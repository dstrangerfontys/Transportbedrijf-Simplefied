using System;
using System.Linq;
using System.Threading.Tasks;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    public class TransportPlanner
    {
        private readonly MySqlConnectionFactory _factory;
        private readonly VoertuigRepository _voertuigen;
        private readonly RitRepository _ritten;

        public TransportPlanner(MySqlConnectionFactory factory, VoertuigRepository vRepo, RitRepository rRepo)
        {
            _factory = factory;
            _voertuigen = vRepo;
            _ritten = rRepo;
        }

        public async Task<Rit?> PlanAsync(RitAanvraag aanvraag)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted) as MySqlTransaction;

            try
            {
                int cap = aanvraag.Type == RitType.Personen
                    ? (aanvraag.AantalPersonen ?? 0)
                    : (aanvraag.GewichtKg ?? 0);

                var kandidaten = await _voertuigen.GetGeschiktEnBeschikbaarAsync(conn, tx!, aanvraag.Type, cap);
                var voertuig = kandidaten.FirstOrDefault();
                if (voertuig is null)
                {
                    await tx!.RollbackAsync();
                    return null;
                }

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
                    gewichtKg: aanvraag.GewichtKg
                );
                rit.BerekenPrijs();

                var newId = await _ritten.AddAsync(conn, tx!, rit);
                rit = new Rit(newId, rit.KlantId, rit.VoertuigId, rit.Datum, rit.Type, rit.AfstandKm,
                              rit.AantalPersonen, rit.GewichtKg, "Gepland", rit.Prijs);

                await tx!.CommitAsync();
                return rit;
            }
            catch
            {
                await tx!.RollbackAsync();
                throw;
            }
        }

        public async Task VoltooiRitAsync(int ritId, int geredenKm)
        {
            using var conn = _factory.Create();
            await conn.OpenAsync();
            using var tx = await conn.BeginTransactionAsync() as MySqlTransaction;

            try
            {
                var rit = await _ritten.GetByIdAsync(conn, tx, ritId);
                if (rit is null) { await tx!.RollbackAsync(); return; }

                // lock voertuig
                using (var lockCmd = new MySqlCommand("SELECT 1 FROM voertuig WHERE voertuigID=@id FOR UPDATE;", conn, tx))
                {
                    lockCmd.Parameters.AddWithValue("@id", rit.VoertuigId);
                    await lockCmd.ExecuteNonQueryAsync();
                }

                // laad voertuig met ordinals
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
                if (v is null) { await tx!.RollbackAsync(); return; }

                int? belading = rit.Type == RitType.Vracht ? rit.GewichtKg : null;
                v.RijdEnSchrijfAf(geredenKm, belading);
                v.Vrijgeven();
                await _voertuigen.UpdateStateAsync(conn, tx!, v);

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