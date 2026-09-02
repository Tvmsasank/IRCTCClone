using RabbitMQ.Client;
using System.Text;

namespace IrctcClone.Infrastructure.Messaging
{
    public class RabbitMqPublisher
    {
        private readonly RabbitMqConnection _rabbitMqConnection;

        public RabbitMqPublisher(
            RabbitMqConnection rabbitMqConnection)
        {
            _rabbitMqConnection = rabbitMqConnection;
        }

        public void Publish(
            string queueName,
            string message)
        {
            using var connection =
                _rabbitMqConnection.CreateConnection();

            using var channel =
                connection.CreateModel();

            channel.QueueDeclare(
                queue: queueName,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var body =
                Encoding.UTF8.GetBytes(message);

            channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: null,
                body: body
            );
        }
    }
}