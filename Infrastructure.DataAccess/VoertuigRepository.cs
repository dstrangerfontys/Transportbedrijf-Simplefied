using System;
using System.Collections.Generic;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    public class VoertuigRepository
    {
        public async Task<List<Voertuig>> GetGeschiktEnBeschikbaarAsync(
            MySqlConnection conn,
            MySqlTransaction tx,
            RitType ritType,
            int vereisteCapaciteit)
        {
            var list = new List<Voertuig>();
            string sql = @"
                SELECT voertuigID, type, capaciteit, kilometerstand, afschrijving, beschikbaar
                FROM voertuig
                WHERE type = @type AND capaciteit >= @cap AND beschikbaar = 1
                FOR UPDATE;";

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@type", ritType == RitType.Personen ? "Personenauto" : "Vrachtauto");
            cmd.Parameters.AddWithValue("@cap", vereisteCapaciteit);

            using var rdr = await cmd.ExecuteReaderAsync();
            // Ordinals bepalen (1x buiten de loop is ook prima)
            int oVoertuigId = rdr.GetOrdinal("voertuigID");
            int oType = rdr.GetOrdinal("type");
            int oCapaciteit = rdr.GetOrdinal("capaciteit");
            int oKilometer = rdr.GetOrdinal("kilometerstand");
            int oAfschrijving = rdr.GetOrdinal("afschrijving");
            int oBeschikbaar = rdr.GetOrdinal("beschikbaar");

            while (await rdr.ReadAsync())
            {
                var typeStr = rdr.IsDBNull(oType) ? "" : rdr.GetString(oType);
                var type = typeStr == "Personenauto" ? VoertuigType.Personenauto : VoertuigType.Vrachtauto;

                list.Add(new Voertuig(
                    id: rdr.GetInt32(oVoertuigId),
                    type: type,
                    capaciteit: rdr.GetInt32(oCapaciteit),
                    kilometerstand: rdr.GetInt32(oKilometer),
                    afschrijvingPercent: rdr.GetDecimal(oAfschrijving),
                    beschikbaar: rdr.GetBoolean(oBeschikbaar) // TINYINT(1) → bool
                ));
            }
            return list;
        }

        public async Task UpdateStateAsync(MySqlConnection conn, MySqlTransaction tx, Voertuig v)
        {
            string sql = @"
                UPDATE voertuig
                SET kilometerstand=@km, afschrijving=@af, beschikbaar=@besch
                WHERE voertuigID=@id;";

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@km", v.Kilometerstand);
            cmd.Parameters.AddWithValue("@af", v.AfschrijvingPercent);
            cmd.Parameters.AddWithValue("@besch", v.Beschikbaar ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", v.Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}