using System.Buffers;
using Microsoft.Graph;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Graph.Models;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace smtp_to_graph;

public class Worker(ILogger<Worker> logger, SmtpServer.SmtpServer smtpServer) : BackgroundService
{
    private readonly ILogger<Worker> _logger = logger;
    private readonly SmtpServer.SmtpServer _smtpServer = smtpServer;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting SMTP server");
        await _smtpServer.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stopping SMTP server");
        _smtpServer.Shutdown();
        return Task.CompletedTask;
    }
}

public class GraphForwardingMessageStore(GraphServiceClient graphServiceClient, ILogger<GraphForwardingMessageStore> logger) : MessageStore, IMessageStore
{
    private readonly GraphServiceClient _graphServiceClient = graphServiceClient;
    private readonly ILogger<GraphForwardingMessageStore> _logger = logger;

    public async override Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        using var loggerScope = _logger.BeginScope("SaveAsync");
        await using var stream = new MemoryStream();

        var position = buffer.GetPosition(0);
        while (buffer.TryGet(ref position, out var memory))
        {
            await stream.WriteAsync(memory, cancellationToken);
        }

        stream.Position = 0;
        var message = await MimeMessage.LoadAsync(stream, cancellationToken);
        _logger.LogInformation("Received message from {from}", message.From);

        await SendMessageAsync(message, cancellationToken);
        return SmtpResponse.Ok;
    }

    private async Task SendMessageAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var bodyContentType = message.Body.ContentType.MimeType;
        _logger.LogDebug("Body content type: {bodyContentType}", bodyContentType);
        var bodySuffix = $"Sent from SMTP Proxy on {Environment.MachineName}{Environment.NewLine}{DateTimeOffset.UtcNow:o}";

        var request = new SendMailPostRequestBody()
        {
            Message = new Message
            {
                Subject = $"Scan from proxy: {message.Subject}",
                Body = new ItemBody
                {
                    ContentType = bodyContentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? BodyType.Html : BodyType.Text,
                    Content = $"{message.TextBody}{Environment.NewLine}{Environment.NewLine}------{Environment.NewLine}{Environment.NewLine}{bodySuffix}"
                },
                ToRecipients = message.To.Mailboxes.Select(x => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = x.Address,
                        Name = x.Name
                    }
                }).ToList(),
                CcRecipients = message.Cc.Mailboxes.Select(x => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = x.Address,
                        Name = x.Name
                    }
                }).ToList(),
                BccRecipients = message.Bcc.Mailboxes.Select(x => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = x.Address,
                        Name = x.Name
                    }
                }).ToList(),
                Attachments = message.Attachments.Select(x => x.ToFileAttachment()).ToList(),
            }
        };

        _logger.LogInformation("Sending message to {to} from {from}", string.Join(", ", request.Message.ToRecipients.Select(x => x.EmailAddress?.Address)), message.From);
        _logger.LogInformation("Attachments: {attachments}", string.Join(", ", request.Message.Attachments.Select(x => x.Name)));
        await _graphServiceClient.Users["scanner@jpd.ms"].SendMail.PostAsync(request, cancellationToken: cancellationToken);
    }
}

public static class Extensions
{
    public static string GetContentType(string contentType)
    {
        return contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? "html" : "text";
    }

    public static byte[] ToAttachment(this MimeEntity mimeEntity)
    {
        using var stream = new MemoryStream();
        mimeEntity.WriteTo(stream);
        System.IO.File.WriteAllBytes("attachment.eml", stream.ToArray());
        return stream.ToArray();
    }

    public static Attachment ToFileAttachment(this MimeEntity attachment)
    {
        using var memory = new MemoryStream();
        if (attachment is MimePart part)
        {
            part.Content.DecodeTo(memory);
        }
        else
        {
            ((MessagePart)attachment).Message.WriteTo(memory);
        }

        return new FileAttachment
        {
            Name = attachment.ContentDisposition?.FileName ?? attachment.ContentType.Name,
            ContentType = attachment.ContentType.MimeType,
            ContentId = attachment.ContentId,
            ContentBytes = memory.ToArray()
        };
    }
}