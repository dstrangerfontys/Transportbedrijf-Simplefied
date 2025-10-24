using System;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    /// Data-toegang voor de tabel 'rit'.
    /// Schrijft/Leest ritten en houdt dit strikt bij één transactie en connectie,
    /// die van buitenaf worden meegegeven (geen eigen verbinding openen hier).
    public class RitRepository
    {
        /// Voegt een nieuwe rit toe en retourneert het nieuwe ID (LAST_INSERT_ID()).
        public async Task<int> AddAsync(MySqlConnection conn, MySqlTransaction tx, Rit rit)
        {
            // SQL met parameters (voorkomt SQL-injectie; duidelijke kolomnamen)
            string sql = @"
                INSERT INTO rit (klantID, voertuigID, datum, afstand, type, aantalPersonen, gewicht, omvang, prijs, status)
                VALUES (@klant, @voertuig, @datum, @afstand, @type, @aantal, @gewicht, NULL, @prijs, @status);
                SELECT LAST_INSERT_ID();";

            using var cmd = new MySqlCommand(sql, conn, tx);

            // Koppelen van parameters uit het domeinobject (Rit) naar SQL
            cmd.Parameters.AddWithValue("@klant", rit.KlantId);
            cmd.Parameters.AddWithValue("@voertuig", rit.VoertuigId);
            cmd.Parameters.AddWithValue("@datum", rit.Datum);
            cmd.Parameters.AddWithValue("@afstand", rit.AfstandKm);

            // DB bewaart 'type' als string: "Personen" of "Vracht"
            cmd.Parameters.AddWithValue("@type", rit.Type == RitType.Personen ? "Personen" : "Vracht");

            // Nullable velden netjes als NULL wegschrijven wanneer niet aanwezig
            cmd.Parameters.AddWithValue("@aantal", (object?)rit.AantalPersonen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gewicht", (object?)rit.GewichtKg ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@prijs", rit.Prijs);

            // Status moet altijd een geldige waarde zijn (bijv. "Gepland")
            cmd.Parameters.AddWithValue("@status", rit.Status);

            // ExecuteScalarAsync om het ID terug te krijgen
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// Haalt één rit op via zijn ID. Retourneert null als niet gevonden.
        public async Task<Rit?> GetByIdAsync(MySqlConnection conn, MySqlTransaction? tx, int ritId)
        {
            using var cmd = new MySqlCommand("SELECT * FROM rit WHERE ritID=@id;", conn, tx);
            cmd.Parameters.AddWithValue("@id", ritId);

            // Reader gebruiken om rij voor rij te kunnen opbouwen naar een domeinobject
            using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return null;

            // Ordinals (kolomindexen) 1x opvragen is iets sneller dan telkens op naam
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

            // Type-string vertalen naar onze domein-enum
            var typeStr = rdr.IsDBNull(oType) ? "" : rdr.GetString(oType);
            var type = typeStr == "Personen" ? RitType.Personen : RitType.Vracht;

            // Nullable kolommen veilig uitlezen
            int? aantal = rdr.IsDBNull(oAantalPersonen) ? (int?)null : rdr.GetInt32(oAantalPersonen);
            int? gewicht = rdr.IsDBNull(oGewicht) ? (int?)null : rdr.GetInt32(oGewicht);

            // Domeinobject reconstrueren uit DB-waarden
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

        /// Update de prijs en de status van een bestaande rit.
        public async Task UpdateAsync(MySqlConnection conn, MySqlTransaction tx, Rit rit)
        {
            // Alleen velden updaten die functioneel mogen veranderen na plannen
            using var cmd = new MySqlCommand("UPDATE rit SET prijs=@p, status=@s WHERE ritID=@id;", conn, tx);
            cmd.Parameters.AddWithValue("@p", rit.Prijs);
            cmd.Parameters.AddWithValue("@s", rit.Status);
            cmd.Parameters.AddWithValue("@id", rit.Id);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}