using RabbitMQ.Client;

namespace IrctcClone.Infrastructure.Messaging
{
    public class RabbitMqConnection
    {
        private readonly ConnectionFactory _factory;

        public RabbitMqConnection()
        {
            _factory = new ConnectionFactory()
            {
                HostName = "localhost",
                UserName = "admin",
                Password = "Admin@123"
            };
        }

        public IConnection CreateConnection()
        {
            return _factory.CreateConnection();
        }
    }
}