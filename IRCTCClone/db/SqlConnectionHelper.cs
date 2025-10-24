using Microsoft.Data.SqlClient;

namespace IrctcClone.db
{
    public class SqlConnectionHelper
    {
        private readonly string _connectionString;

        public SqlConnectionHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}
