using System.Buffers;
using Microsoft.Graph;
using Microsoft.Graph.Users.Item.SendMail;
using Microsoft.Graph.Models;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace smtp_to_graph;

public class GraphForwardingMessageStore(GraphServiceClient graphServiceClient, ILogger<GraphForwardingMessageStore> logger) : MessageStore, IMessageStore
{
    private readonly GraphServiceClient _graphServiceClient = graphServiceClient;
    private readonly ILogger<GraphForwardingMessageStore> _logger = logger;
    private readonly string _senderEmail = "scanner@jpd.ms";

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
                    Content = $"{message.TextBody}{Environment.NewLine}{Environment.NewLine}------{Environment.NewLine}{bodySuffix}"
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
                //Attachments = message.Attachments.Select(x => x.ToFileAttachment()).ToList(),
            }
        };

        var messageRequest = await _graphServiceClient.Users[_senderEmail].Messages.PostAsync(request.Message, cancellationToken: cancellationToken);

        if (message.Attachments.Any())
        {
            var attachments = new List<FileAttachment>();
            foreach (var attachment in message.Attachments)
            {
                var attachmentItem = new AttachmentItem
                {
                    AttachmentType = AttachmentType.File,
                    Name = fileName,
                    Size = attachmentContentSize,
                };

                //initiate the upload session for large files (note that this example doesn't handle multiple attachment).
                var uploadSession = await _graphServiceClient.Users[_senderEmail].Messages[messageRequest?.Id].Attachments
                                                                        .CreateUploadSession(attachmentItem)
                                                                        .Request()
                                                                        .PostAsync();
            }
            request.Message.Attachments = attachments;
        }


        _logger.LogInformation("Sending message to {to} from {from}", string.Join(", ", request.Message.ToRecipients.Select(x => x.EmailAddress?.Address)), message.From);
        _logger.LogInformation("Attachments: {attachments}", string.Join(", ", request.Message.Attachments.Select(x => x.Name)));
        await _graphServiceClient.Users["scanner@jpd.ms"].SendMail.PostAsync(request, cancellationToken: cancellationToken);
    }

    private async Task HandleLargeAttachment(Stream memory)
    {
        if (memory.Length > 3 * 1024 * 1024)
        {
            //first create an email draft
            var msgResult = await graphClient.Users[senderEmail].Messages
                                    .Request()
                                    .AddAsync(message);

            var attachmentItem = new AttachmentItem
            {
                AttachmentType = AttachmentType.File,
                Name = fileName,
                Size = attachmentContentSize,
            };

            //initiate the upload session for large files (note that this example doesn't handle multiple attachment).
            var uploadSession = await graphClient.Users[senderEmail].Messages[msgResult.Id].Attachments
                                                                    .CreateUploadSession(attachmentItem)
                                                                    .Request()
                                                                    .PostAsync();

            var maxChunkSize = 1024 * 320;
            var allBytes = System.IO.File.ReadAllBytes(path);

            using (var stream = new MemoryStream(allBytes))
            {
                stream.Position = 0;
                LargeFileUploadTask<FileAttachment> largeFileUploadTask = new LargeFileUploadTask<FileAttachment>(uploadSession, stream, maxChunkSize);

                await largeFileUploadTask.UploadAsync();
            }
            //once uploaded, sends out the email from the Draft folder
            await graphClient.Users[senderEmail].Messages[msgResult.Id].Send().Request().PostAsync();
            throw new InvalidOperationException("Attachment too large");
        }
    }
}
