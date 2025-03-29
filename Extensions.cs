using Microsoft.Graph.Models;
using MimeKit;

namespace smtp_to_graph;

public static class Extensions
{
    public static string GetContentType(string contentType)
    {
        return contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ? "html" : "text";
    }

    public static MemoryStream ToStream(this MimeEntity mimeEntity)
    {
        using var stream = new MemoryStream();
        mimeEntity.WriteTo(stream);
        stream.Position = 0;
        return stream;
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