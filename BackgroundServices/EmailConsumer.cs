using IrctcClone.Infrastructure.Messaging;
using IrctcClone.Services;
using IRCTCClone.Services;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace IrctcClone.BackgroundServices
{
    public class EmailConsumer : BackgroundService
    {
        private readonly RabbitMqConnection _rabbitMqConnection;
        private readonly IServiceScopeFactory _scopeFactory;

        public EmailConsumer(
            RabbitMqConnection rabbitMqConnection,
            IServiceScopeFactory scopeFactory)
        {
            _rabbitMqConnection = rabbitMqConnection;
            _scopeFactory = scopeFactory;
        }

        protected override Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            var connection =
                _rabbitMqConnection.CreateConnection();

            var channel =
                connection.CreateModel();

            channel.QueueDeclare(
                queue: "email-queue",
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var consumer =
                new EventingBasicConsumer(channel);

            consumer.Received += async (model, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();

                    var message =
                        Encoding.UTF8.GetString(body);

                    var bookingData =
                        JsonSerializer.Deserialize<EmailQueueModel>(message);

                    using var scope =
                        _scopeFactory.CreateScope();

                    var emailService =
                        scope.ServiceProvider
                        .GetRequiredService<EmailService>();


                    var result = emailService.GeneratePdf(
                        bookingData.BookingId,
                        bookingData.UserId.ToString()
                    );

                    var pdfBytes = result.pdfBytes;

                    var pnr = result.PNR;

                    // DEMO EMAIL SEND
                    await emailService.SendEmailWithAttachment(
                        bookingData.Email,
                        "IRCTC Booking Confirmation",
                        $"Your Ticket has been confirmed. Your PNR is {pnr}",
                        pdfBytes,
                        $"{pnr}.pdf"
                    );

                    // ACKNOWLEDGE MESSAGE
                    channel.BasicAck(
                        deliveryTag: ea.DeliveryTag,
                        multiple: false);
                }
                catch
                {
                    // optional logging
                }
            };

            channel.BasicConsume(
                queue: "email-queue",
                autoAck: false,
                consumer: consumer);

            return Task.CompletedTask;
        }
    }

    public class EmailQueueModel
    {
        public int BookingId { get; set; }

        public int UserId { get; set; }

        public string Email { get; set; }

        public string PNR { get; set; }
    }
}