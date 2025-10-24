using System;
using System.Collections.Generic;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    /// Data-toegang voor de tabel 'voertuig'.
    /// Zoekt geschikte/beschikbare voertuigen en werkt voertuigstaat (km/afschrijving/beschikbaar) bij.
    public class VoertuigRepository
    {
        /// Haalt alle voertuigen op die:
        /// - van het juiste type zijn (Personenauto/Vrachtauto),
        /// - voldoende capaciteit hebben,
        /// - en beschikbaar zijn.
        /// De SELECT ... FOR UPDATE vergrendelt de rijen in deze transactie om race conditions te voorkomen.
        public async Task<List<Voertuig>> GetGeschiktEnBeschikbaarAsync(
            MySqlConnection conn,
            MySqlTransaction tx,
            RitType ritType,
            int vereisteCapaciteit)
        {
            var list = new List<Voertuig>();

            // FOR UPDATE: belangrijk bij planning, voorkomt dat twee transacties hetzelfde voertuig kiezen
            string sql = @"
                SELECT voertuigID, type, capaciteit, kilometerstand, afschrijving, beschikbaar
                FROM voertuig
                WHERE type = @type AND capaciteit >= @cap AND beschikbaar = 1
                FOR UPDATE;";

            using var cmd = new MySqlCommand(sql, conn, tx);

            // In de DB is 'type' een string-veld: "Personenauto" of "Vrachtauto"
            cmd.Parameters.AddWithValue("@type", ritType == RitType.Personen ? "Personenauto" : "Vrachtauto");
            cmd.Parameters.AddWithValue("@cap", vereisteCapaciteit);

            using var rdr = await cmd.ExecuteReaderAsync();

            // Ordinals eenmaal bepalen voor performance (niet verplicht, wel netter)
            int oVoertuigId = rdr.GetOrdinal("voertuigID");
            int oType = rdr.GetOrdinal("type");
            int oCapaciteit = rdr.GetOrdinal("capaciteit");
            int oKilometer = rdr.GetOrdinal("kilometerstand");
            int oAfschrijving = rdr.GetOrdinal("afschrijving");
            int oBeschikbaar = rdr.GetOrdinal("beschikbaar");

            while (await rdr.ReadAsync())
            {
                // Type-string naar enum
                var typeStr = rdr.IsDBNull(oType) ? "" : rdr.GetString(oType);
                var type = typeStr == "Personenauto" ? VoertuigType.Personenauto : VoertuigType.Vrachtauto;

                // Domeinobject opbouwen
                list.Add(new Voertuig(
                    id: rdr.GetInt32(oVoertuigId),
                    type: type,
                    capaciteit: rdr.GetInt32(oCapaciteit),
                    kilometerstand: rdr.GetInt32(oKilometer),
                    afschrijvingPercent: rdr.GetDecimal(oAfschrijving),
                    beschikbaar: rdr.GetBoolean(oBeschikbaar) // MySQL TINYINT(1) mapt naar bool
                ));
            }
            return list;
        }

        /// Slaat wijzigingen op aan een voertuig:
        /// - kilometerstand
        /// - afschrijving
        /// - beschikbaarheid (bijv. reserveren of vrijgeven)
        public async Task UpdateStateAsync(MySqlConnection conn, MySqlTransaction tx, Voertuig v)
        {
            string sql = @"
                UPDATE voertuig
                SET kilometerstand=@km, afschrijving=@af, beschikbaar=@besch
                WHERE voertuigID=@id;";

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@km", v.Kilometerstand);
            cmd.Parameters.AddWithValue("@af", v.AfschrijvingPercent);
            cmd.Parameters.AddWithValue("@besch", v.Beschikbaar ? 1 : 0); // boolean → 1/0
            cmd.Parameters.AddWithValue("@id", v.Id);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}