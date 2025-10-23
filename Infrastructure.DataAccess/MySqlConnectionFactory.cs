using MySql.Data.MySqlClient;

namespace Infrastructure.DataAccess;

public class MySqlConnectionFactory
{
    private readonly string _cs;
    public MySqlConnectionFactory(string connectionString) => _cs = connectionString;
    public MySqlConnection Create() => new MySqlConnection(_cs);
}
