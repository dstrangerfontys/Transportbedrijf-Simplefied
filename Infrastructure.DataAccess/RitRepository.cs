using System;
using Core.Domain;
using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess
{
    /// Data-toegang voor de tabel 'rit'.
    /// Schrijft/Leest ritten en houdt dit strikt bij één transactie en connectie,
    /// die van buitenaf worden meegegeven.
    public class RitRepository
    {
        /// Veilige mapper: zet de DB-string (rit.type) om naar RitType enum.
        /// Gooit een exception als de waarde onbekend is.
        private static RitType MapRitType(string? dbValue)
        {
            var s = (dbValue ?? string.Empty).Trim();
            return s switch
            {
                "Personen" => RitType.Personen,
                "Vracht" => RitType.Vracht,
                _ => throw new InvalidOperationException(
                        $"Onbekende waarde voor kolom 'rit.type' uit de database: '{dbValue}'. " +
                        "Verwacht 'Personen' of 'Vracht'."
                    )
            };
        }

        /// Voegt een nieuwe rit toe en retourneert het nieuwe ID (LAST_INSERT_ID()).
        public async Task<int> AddAsync(MySqlConnection conn, MySqlTransaction tx, Rit rit)
        {
            // SQL met parameters
            const string sql = @"
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

            // Nullable velden als NULL wegschrijven wanneer niet aanwezig
            cmd.Parameters.AddWithValue("@aantal", (object?)rit.AantalPersonen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gewicht", (object?)rit.GewichtKg ?? DBNull.Value);

            // Prijs & status
            cmd.Parameters.AddWithValue("@prijs", rit.Prijs);
            cmd.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(rit.Status) ? "Gepland" : rit.Status);

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

            // Kolomindexen 1x opvragen
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

            // Type-string vertalen naar domein-enum
            var typeStr = rdr.IsDBNull(oType) ? null : rdr.GetString(oType);
            var type = MapRitType(typeStr);

            // Nullable kolommen uitlezen
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
            // Alleen velden updaten die mogen veranderen na plannen
            const string sql = "UPDATE rit SET prijs=@p, status=@s WHERE ritID=@id;";
            using var cmd = new MySqlCommand(sql, conn, tx);

            cmd.Parameters.AddWithValue("@p", rit.Prijs);
            cmd.Parameters.AddWithValue("@s", string.IsNullOrWhiteSpace(rit.Status) ? "Gepland" : rit.Status);
            cmd.Parameters.AddWithValue("@id", rit.Id);

            await cmd.ExecuteNonQueryAsync();
        }

        /// Logt een afgewezen rit-aanvraag in de Database.
        /// Vereist dat 'voertuigID' NULL mag zijn in de tabel 'rit'.
        public async Task<int> AddRejectedAsync(
            MySqlConnection conn,
            MySqlTransaction tx,
            int klantId,
            DateTime datum,
            RitType type,
            int afstandKm,
            int? aantalPersonen,
            int? gewichtKg)
        {
            const string sql = @"
                INSERT INTO rit (klantID, voertuigID, datum, afstand, type, aantalPersonen, gewicht, omvang, prijs, status)
                VALUES (@klant, NULL, @datum, @afstand, @type, @aantal, @gewicht, NULL, @prijs, 'Afgewezen');
                SELECT LAST_INSERT_ID();";

            using var cmd = new MySqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("@klant", klantId);
            cmd.Parameters.AddWithValue("@datum", datum);
            cmd.Parameters.AddWithValue("@afstand", afstandKm);
            cmd.Parameters.AddWithValue("@type", type == RitType.Personen ? "Personen" : "Vracht");
            cmd.Parameters.AddWithValue("@aantal", (object?)aantalPersonen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@gewicht", (object?)gewichtKg ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@prijs", 0m); // Afgewezen = geen prijs

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
    }
}