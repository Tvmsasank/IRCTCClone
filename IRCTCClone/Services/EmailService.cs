using IRCTCClone.Models;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace IRCTCClone.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly string _connectionString;


        public EmailService(IConfiguration config)
        {
            _config = config;

            _connectionString = _config.GetConnectionString("DefaultConnection");
        }

        public async Task SendEmailWithAttachment(string toEmail, string subject, string body, byte[] attachmentBytes, string attachmentName)
        {
            var smtpServer = _config["EmailSettings:SmtpServer"];
            var smtpPort = int.Parse(_config["EmailSettings:SmtpPort"]);
            var senderEmail = _config["EmailSettings:SenderEmail"];
            var senderName = _config["EmailSettings:SenderName"];
            var username = _config["EmailSettings:Username"];
            var password = _config["EmailSettings:Password"];
            using (var client = new SmtpClient(smtpServer, smtpPort))
            {
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(username, password);

                var message = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                message.To.Add(toEmail);

                // Add PDF attachment
                if (attachmentBytes != null)
                {
                    var attachmentStream = new MemoryStream(attachmentBytes);
                    message.Attachments.Add(new Attachment(attachmentStream, attachmentName, "application/pdf"));
                }

                await client.SendMailAsync(message);
            }
        }


        public async Task SendEmail(string toEmail, string subject, string body)
        {
            var smtpServer = _config["EmailSettings:SmtpServer"];
            var smtpPort = int.Parse(_config["EmailSettings:SmtpPort"]);
            var senderEmail = _config["EmailSettings:SenderEmail"];
            var senderName = _config["EmailSettings:SenderName"];
            var username = _config["EmailSettings:Username"];
            var password = _config["EmailSettings:Password"];

            using (var client = new SmtpClient(smtpServer, smtpPort))
            {
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(username, password);

                var message = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                message.To.Add(toEmail);

                await client.SendMailAsync(message);
            }
        }




        public byte[] GeneratePdf(int bookingId)
        {
         
            var booking = GetBookingDetails(bookingId);
            if (booking == null) return null;

            using (var ms = new MemoryStream())
            {
                // Document setup
                var doc = new iTextSharp.text.Document(PageSize.A4, 36, 36, 36, 36);
                var writer = PdfWriter.GetInstance(doc, ms);
                writer.CloseStream = false;
                doc.Open();

                // Fonts
                var titleFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 18);
                var sectionWhite = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 11, BaseColor.WHITE);
                var sectionBlue = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, new BaseColor(0, 102, 204));
                var labelFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10);
                var normal = FontFactory.GetFont(FontFactory.HELVETICA, 9);
                var small = FontFactory.GetFont(FontFactory.HELVETICA, 8);

                // Colors
                var premiumBlue = new BaseColor(0, 102, 204);
                var lightPanel = new BaseColor(245, 246, 250);
                var frameColor = BaseColor.BLACK;

                var cb = writer.DirectContent;

                // --- Page border/frame ---
                Rectangle page = doc.PageSize;
                page = new Rectangle(doc.PageSize.Left + 15, doc.PageSize.Bottom + 15, doc.PageSize.Right - 15, doc.PageSize.Top - 15);
                page.Border = Rectangle.BOX;
                page.BorderWidth = 1.0f;
                page.BorderColor = frameColor;
                page.GetLeft(page.Left);
                doc.Add(new Chunk()); // ensure content started
                cb.Rectangle(page.Left, page.Bottom, page.Width, page.Height);
                cb.SetLineWidth(1.2f);
                cb.SetColorStroke(frameColor);
                cb.Stroke();

                // --- Light background panel behind ticket content ---
                cb.SetColorFill(lightPanel);
                float panelX = doc.Left + 8;
                float panelY = doc.Top - 300; // adjust height start
                float panelW = doc.PageSize.Width - doc.Left - doc.Right - 0;
                float panelH = 360f;
                cb.RoundRectangle(panelX, panelY, panelW, panelH, 6f);
                cb.Fill();

                // --- Watermark (faint, centered, rotated) ---
                cb.SaveState();
                cb.SetGState(new PdfGState { FillOpacity = 0.06f, StrokeOpacity = 0.06f });
                var wmFont = FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 60);
                ColumnText.ShowTextAligned(cb, Element.ALIGN_CENTER, new Phrase("IRCTC CLONE", wmFont),
                    doc.PageSize.Width / 2, doc.PageSize.Height / 2, 45);
                cb.RestoreState();

                // --- Header: logo + title + PNR/Date
                var headerTbl = new PdfPTable(3) { WidthPercentage = 100f };
                headerTbl.SetWidths(new float[] { 1f, 3f, 1.7f });

                // Logo
                string logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "irctc_logo.png");
                if (System.IO.File.Exists(logoPath))
                {
                    var logo = iTextSharp.text.Image.GetInstance(logoPath);
                    logo.ScaleToFit(55f, 55f);
                    headerTbl.AddCell(new PdfPCell(logo)
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        VerticalAlignment = Element.ALIGN_MIDDLE,
                        PaddingLeft = 2f
                    });
                }
                else
                {
                    headerTbl.AddCell(new PdfPCell(new Phrase("IRCTC", titleFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_LEFT,
                        VerticalAlignment = Element.ALIGN_MIDDLE
                    });
                }

                // Title (center)
                headerTbl.AddCell(new PdfPCell(new Phrase("E - TICKET", titleFont))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    VerticalAlignment = Element.ALIGN_MIDDLE,
                    PaddingTop = 12f
                });

                // Right PNR + Date (proper blue color)
                var right = new PdfPTable(1) { WidthPercentage = 100f };

                right.AddCell(new PdfPCell(new Phrase($"PNR: {booking.PNR}",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12, BaseColor.BLUE)))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                });

                right.AddCell(new PdfPCell(new Phrase($"Booked On: {booking.BookingDate:dd-MMM-yyyy}",
                    FontFactory.GetFont(FontFactory.HELVETICA, 10)))
                {
                    Border = Rectangle.NO_BORDER,
                    HorizontalAlignment = Element.ALIGN_RIGHT
                });

                headerTbl.AddCell(new PdfPCell(right)
                {
                    Border = Rectangle.NO_BORDER,
                    PaddingTop = 5f
                });

                doc.Add(headerTbl);
                doc.Add(new Paragraph("\n"));


                // --- Top info table: Train/Route/Status and QR/barcode
                var topTbl = new PdfPTable(2) { WidthPercentage = 100f, SpacingBefore = 6f };
                topTbl.SetWidths(new float[] { 2.7f, 1f });

                // Left: Train details
                var leftTbl = new PdfPTable(2) { WidthPercentage = 100f };
                leftTbl.DefaultCell.Border = Rectangle.NO_BORDER;
                leftTbl.AddCell(new PdfPCell(new Phrase("Train", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.TrainNumber} - {booking.TrainName}", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("From", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.Frmst} ({booking.Frmst?.Split(' ').FirstOrDefault()})", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("To", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.Tost} ({booking.Tost?.Split(' ').FirstOrDefault()})", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("Journey Date", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase(booking.JourneyDate.ToString("dd-MMM-yyyy"), normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("Class / Quota", labelFont)) { Border = Rectangle.NO_BORDER });
                leftTbl.AddCell(new PdfPCell(new Phrase($"{booking.ClassCode} / {booking.Quota}", normal)) { Border = Rectangle.NO_BORDER });

                leftTbl.AddCell(new PdfPCell(new Phrase("Status", labelFont)) { Border = Rectangle.NO_BORDER });
                var statusCell = new PdfPCell(new Phrase(booking.Status, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10, booking.Status == "CONFIRMED" ? BaseColor.GREEN : BaseColor.RED))) { Border = Rectangle.NO_BORDER };
                leftTbl.AddCell(statusCell);

                topTbl.AddCell(leftTbl);

                // Right: QR + Barcode
                var rightTbl = new PdfPTable(1) { WidthPercentage = 100f };



                // QR code (PNR + Train + Date)
                // Build passenger details
                string passengerInfo = string.Join(";", booking.Passengers.Select(p =>
                    $"{p.Name} | {p.Age} | {p.Gender} | {p.SeatNumber}"
                ));
                // Build QR text
                string qrText =
                $"PNR: {booking.PNR} \nTrain: {booking.TrainNumber} - {booking.TrainName} \nFrom: {booking.Frmst} \nTo: {booking.Tost} \nDOJ: {booking.JourneyDate:yyyy-MM-dd} \nQuota: {booking.Quota} \nPassenger Details: \n\n{passengerInfo}";
                var qr = new BarcodeQRCode(qrText, 150, 150, null);
                var qrImage = qr.GetImage();
                qrImage.ScaleToFit(110f, 110f);
                var qrCell = new PdfPCell(qrImage) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_RIGHT, Padding = 2f };
                rightTbl.AddCell(qrCell);

                topTbl.AddCell(rightTbl);

                doc.Add(topTbl);
                doc.Add(new Paragraph("\n"));

                // --- Passenger table (premium style) ---
                var passTbl = new PdfPTable(6) { WidthPercentage = 100f, SpacingBefore = 6f };
                passTbl.SetWidths(new float[] { 3f, 1f, 1f, 1.4f, 1.4f, 1.8f });

                // Header row with blue background
                var hdrCell = new PdfPCell(new Phrase("Passenger Details", sectionWhite))
                {
                    BackgroundColor = premiumBlue,
                    Colspan = 6,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 6f,
                    Border = Rectangle.NO_BORDER
                };
                passTbl.AddCell(hdrCell);

                // Column headers
                var cols = new[] { "Name", "Age", "Gender", "Coach", "Seat", "Berth" };
                foreach (var c in cols)
                {
                    passTbl.AddCell(new PdfPCell(new Phrase(c, labelFont)) { BackgroundColor = BaseColor.LIGHT_GRAY, HorizontalAlignment = Element.ALIGN_CENTER, Padding = 4f });
                }

                // Rows
                foreach (var p in booking.Passengers)
                {
                   

                    string seatNo = p.SeatNumber.Length > 0 ? p.SeatNumber.Substring(0, 1) : "-";
                    string coach = p.SeatPrefix;
          
                   /* coach = p.SeatPrefix;
                    seatNo = p.SeatNumber.Length > 0 ? p.SeatNumber.Substring(0, 1) : "-";*/
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Name, normal)) { Padding = 4f });
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Age.ToString(), normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Gender, normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(coach, normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(seatNo, normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                    passTbl.AddCell(new PdfPCell(new Phrase(p.Berth ?? "-", normal)) { HorizontalAlignment = Element.ALIGN_CENTER });
                }

                doc.Add(passTbl);
                doc.Add(new Paragraph("\n"));

                // --- Payment Summary (Appears immediately after Passenger table) ---
                var payTbl = new PdfPTable(2) { WidthPercentage = 50f, HorizontalAlignment = Element.ALIGN_LEFT };
                payTbl.SetWidths(new float[] { 2f, 1f });

                // Header
                payTbl.AddCell(new PdfPCell(new Phrase("Payment Summary", sectionWhite))
                {
                    BackgroundColor = premiumBlue,
                    Colspan = 2,
                    HorizontalAlignment = Element.ALIGN_CENTER,
                    Padding = 6f,
                    Border = Rectangle.NO_BORDER
                });

                // Function to add rows
                void AddPay(string label, string val)
                {
                    payTbl.AddCell(new PdfPCell(new Phrase(label, labelFont)) { Border = Rectangle.NO_BORDER, Padding = 5f });
                    payTbl.AddCell(new PdfPCell(new Phrase(val, normal)) { Border = Rectangle.NO_BORDER, Padding = 5f, HorizontalAlignment = Element.ALIGN_RIGHT });
                }

                AddPay("Base Fare:", $"₹ {booking.BaseFare:F2}");
                AddPay("GST (5%):", $"₹ {booking.GST:F2}");
                AddPay("Quota Charge:", $"₹ {booking.QuotaCharge:F2}");
                AddPay("Surge:", $"₹ {booking.SurgeAmount:F2}");

                // Line + Total Fare
                payTbl.AddCell(new PdfPCell(new Phrase("")) { Border = Rectangle.TOP_BORDER, BorderWidthTop = 0.7f, Colspan = 2, Padding = 6f });
                AddPay("Total Paid:", $"₹ {booking.TotalFare:F2}");

                doc.Add(payTbl);

                // Push Signature near bottom of page
                var sigTable = new PdfPTable(1);
                sigTable.TotalWidth = 200f;

                string sigPathF = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "signature_placeholder.png");
                if (System.IO.File.Exists(sigPathF))
                {
                    var sImg = Image.GetInstance(sigPathF);
                    sImg.ScaleToFit(120f, 40f);
                    sigTable.AddCell(new PdfPCell(sImg)
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }
                else
                {
                    sigTable.AddCell(new PdfPCell(new Phrase("Authorized Signatory", labelFont))
                    {
                        Border = Rectangle.NO_BORDER,
                        HorizontalAlignment = Element.ALIGN_CENTER
                    });
                }

                sigTable.AddCell(new PdfPCell(new Phrase("IRCTC Clone", small)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_CENTER });
                sigTable.AddCell(new PdfPCell(new Phrase($"PNR: {booking.PNR}", small)) { Border = Rectangle.NO_BORDER, HorizontalAlignment = Element.ALIGN_CENTER });

                // FIXED POSITION (BOTTOM RIGHT)
                sigTable.WriteSelectedRows(0, -1, doc.PageSize.Width - 220, 140, writer.DirectContent);


                // --- Terms & Conditions box ---
                // ---- TERMS & CONDITIONS (Fixed Position Above Footer) ----
                var termsText = new Paragraph();
                termsText.Add(new Chunk("Terms & Conditions\n",
                    FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 10)));
                termsText.Add(new Chunk("• Carry valid ID.\n", small));
                termsText.Add(new Chunk("• Boarding time subject to announcement.\n", small));
                termsText.Add(new Chunk("• Ticket is non-transferable.\n", small));
                termsText.Add(new Chunk("• Berth/coach may change by railway authority.\n", small));
                termsText.Add(new Chunk("• Refund rules as per IRCTC guidelines.\n", small));

                ColumnText ctTerms = new ColumnText(cb);

                // LEFT, BOTTOM-Y, RIGHT, TOP-Y
                ctTerms.SetSimpleColumn(
                    40,                                // X-left
                    doc.PageSize.GetBottom(60),        // bottom (80 px above bottom)
                    doc.PageSize.Width - 40,           // right
                    doc.PageSize.GetBottom(160)        // top of T&C block
                );
                ctTerms.AddElement(termsText);
                ctTerms.Go();
                cb.SetLineWidth(0.5f);
                cb.SetColorStroke(BaseColor.GRAY);

                // Draw line just above footer
                cb.MoveTo(40, doc.PageSize.GetBottom(60));
                cb.LineTo(doc.PageSize.Width - 40, doc.PageSize.GetBottom(60));
                cb.Stroke();

                // --- Footer small print centered
                cb.BeginText();

                var footerFont = FontFactory.GetFont(FontFactory.HELVETICA, 8);
                cb.SetFontAndSize(footerFont.BaseFont, 8);

                float centerX = (doc.PageSize.Left + doc.PageSize.Right) / 2;
                float footerY = doc.PageSize.GetBottom(40);

                cb.ShowTextAligned(
                    Element.ALIGN_CENTER,
                    $"Generated on {DateTime.UtcNow:dd-MMM-yyyy HH:mm} UTC | IRCTC Clone",
                    centerX,
                    footerY,
                    0
                );

                cb.EndText();

                // finalize
                doc.Close();
                writer.Flush();

                ms.Position = 0;
                var fileName = $"{booking.PNR}_{booking.TrainName}_{booking.TrainNumber}.pdf";
         
                return ms.ToArray();
            }
        }


        private Booking GetBookingDetails(int bookingId)
        {
            Booking booking = null;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("spGetBookingDetails", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@BookingId", bookingId);

                    using (var reader = cmd.ExecuteReader())
                    {
                        // --- Booking info ---
                        if (reader.Read())
                        {
                            booking = new Booking
                            {
                                Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                                UserId = reader["UserId"]?.ToString(),
                                PNR = reader["PNR"]?.ToString(),
                                TrainName = reader["TrainName"]?.ToString(),
                                TrainNumber = reader["TrainNumber"] != DBNull.Value ? Convert.ToInt32(reader["TrainNumber"]) : 0,
                                Frmst = reader["Frmst"]?.ToString(),
                                Tost = reader["Tost"]?.ToString(),
                                JourneyDate = reader["JourneyDate"] != DBNull.Value ? Convert.ToDateTime(reader["JourneyDate"]) : DateTime.MinValue,
                                BookingDate = reader["BookingDate"] != DBNull.Value ? Convert.ToDateTime(reader["BookingDate"]) : DateTime.MinValue,
                                Status = reader["Status"]?.ToString(),
                                ClassCode = reader["ClassCode"]?.ToString(),
                                SeatPrefix = reader["SeatPrefix"]?.ToString(),
                                Quota = reader["Quota"]?.ToString(),
                                BaseFare = reader["BaseFare"] != DBNull.Value ? Convert.ToDecimal(reader["BaseFare"]) : 0,
                                GST = reader["GST"] != DBNull.Value ? Convert.ToDecimal(reader["GST"]) : 0,
                                SurgeAmount = reader["SurgeAmount"] != DBNull.Value ? Convert.ToDecimal(reader["SurgeAmount"]) : 0,
                                TotalFare = reader["TotalFare"] != DBNull.Value ? Convert.ToDecimal(reader["TotalFare"]) : 0,
                                QuotaCharge = reader["QuotaCharge"] != DBNull.Value ? Convert.ToDecimal(reader["QuotaCharge"]) : 0,
                                Passengers = new List<Passenger>()
                            };
                        }

                        if (reader.NextResult())
                        {
                            while (reader.Read())
                            {
                                booking.Passengers.Add(new Passenger
                                {
                                    Name = reader["Name"]?.ToString(),
                                    Age = reader["Age"] != DBNull.Value ? Convert.ToInt32(reader["Age"]) : 0,
                                    Gender = reader["Gender"]?.ToString(),
                                    SeatNumber = reader["SeatNumber"]?.ToString(),
                                    Berth = reader["Berth"]?.ToString(),
                                    SeatPrefix=booking.SeatPrefix
                                });
                            }
                        }
                    }
                }
            }

            return booking;
        }

    }

}
