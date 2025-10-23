using System;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    public class RitRepository
    {
        public async Task<int> AddAsync(MySqlConnection conn, MySqlTransaction tx, Rit rit)
        {
            string sql = @"
                INSERT INTO rit (klantID, voertuigID, datum, afstand, type, aantalPersonen, gewicht, omvang, prijs, status)
                VALUES (@klant, @voertuig, @datum, @afstand, @type, @aantal, @gewicht, NULL, @prijs, @status);
                SELECT LAST_INSERT_ID();";

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@klant", rit.KlantId);
            cmd.Parameters.AddWithValue("@voertuig", rit.VoertuigId);
            cmd.Parameters.AddWithValue("@datum", rit.Datum);
            cmd.Parameters.AddWithValue("@afstand", rit.AfstandKm);
            cmd.Parameters.AddWithValue("@type", rit.Type == RitType.Personen ? "Personen" : "Vracht");
            cmd.Parameters.AddWithValue("@aantal", (object?)rit.AantalPersonen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gewicht", (object?)rit.GewichtKg ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@prijs", rit.Prijs);
            cmd.Parameters.AddWithValue("@status", rit.Status);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<Rit?> GetByIdAsync(MySqlConnection conn, MySqlTransaction? tx, int ritId)
        {
            using var cmd = new MySqlCommand("SELECT * FROM rit WHERE ritID=@id;", conn, tx);
            cmd.Parameters.AddWithValue("@id", ritId);

            using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return null;

            // Ordinals ophalen
            int oRitId = rdr.GetOrdinal("ritID");
            int oKlantId = rdr.GetOrdinal("klantID");
            int oVoertuigId = rdr.GetOrdinal("voertuigID");
            int oDatum = rdr.GetOrdinal("datum");
            int oAfstand = rdr.GetOrdinal("afstand");
            int oType = rdr.GetOrdinal("type");
            int oAantalPersonen = rdr.GetOrdinal("aantalPersonen");
            int oGewicht = rdr.GetOrdinal("gewicht");
            int oStatus = rdr.GetOrdinal("status");
            int oPrijs = rdr.GetOrdinal("prijs");

            var typeStr = rdr.IsDBNull(oType) ? "" : rdr.GetString(oType);
            var type = typeStr == "Personen" ? RitType.Personen : RitType.Vracht;

            int? aantal = rdr.IsDBNull(oAantalPersonen) ? (int?)null : rdr.GetInt32(oAantalPersonen);
            int? gewicht = rdr.IsDBNull(oGewicht) ? (int?)null : rdr.GetInt32(oGewicht);

            return new Rit(
                id: rdr.GetInt32(oRitId),
                klantId: rdr.GetInt32(oKlantId),
                voertuigId: rdr.GetInt32(oVoertuigId),
                datum: rdr.GetDateTime(oDatum),
                type: type,
                afstandKm: rdr.GetInt32(oAfstand),
                aantalPersonen: aantal,
                gewichtKg: gewicht,
                status: rdr.IsDBNull(oStatus) ? "Onbekend" : rdr.GetString(oStatus),
                prijs: rdr.GetDecimal(oPrijs)
            );
        }

        public async Task UpdateAsync(MySqlConnection conn, MySqlTransaction tx, Rit rit)
        {
            using var cmd = new MySqlCommand("UPDATE rit SET prijs=@p, status=@s WHERE ritID=@id;", conn, tx);
            cmd.Parameters.AddWithValue("@p", rit.Prijs);
            cmd.Parameters.AddWithValue("@s", rit.Status);
            cmd.Parameters.AddWithValue("@id", rit.Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}