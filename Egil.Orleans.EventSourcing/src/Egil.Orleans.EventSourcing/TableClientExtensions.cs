using Azure;
using System.Net;

namespace Egil.Orleans.EventSourcing;

internal static class TableClientUtilities
{
    public static bool ContainsDisallowedKeyFieldCharacters(this string value)
    {
        // Remove any characters that can't be used in Azure PartitionKey or RowKey values
        // https://learn.microsoft.com/da-dk/rest/api/storageservices/Understanding-the-Table-Service-Data-Model#characters-disallowed-in-key-fields
        for (var i = 0; i < value.Length; i++)
        {
            var found = value[i] switch
            {
                '/' or '\\' or '#' or '?' or '\t' or '\n' or '\r' => true,
                char c when c <= 0x1F || c >= 0x7F && c <= 0x9F => true,
                _ => false,
            };

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    public static string SanitizeKeyPropertyValue(this string key, char sanitizeChar)
    {
        if (key.Length >= 1024)
        {
            throw new ArgumentException($"Key length {key.Length} is too long to be an Azure table key. Key = {key}");
        }

        // Remove any characters that can't be used in Azure PartitionKey or RowKey values
        // https://learn.microsoft.com/da-dk/rest/api/storageservices/Understanding-the-Table-Service-Data-Model#characters-disallowed-in-key-fields
        Span<char> keySpan = stackalloc char[key.Length];
        for (var i = 0; i < key.Length; i++)
        {
            keySpan[i] = key[i] switch
            {
                '/' => sanitizeChar,
                '\\' => sanitizeChar,
                '#' => sanitizeChar,
                '?' => sanitizeChar,
                '\t' => sanitizeChar,
                '\n' => sanitizeChar,
                '\r' => sanitizeChar,
                char c when c <= 0x1F || c >= 0x7F && c <= 0x9F => sanitizeChar,
                _ => key[i],
            };
        }

        return keySpan.ToString();
    }

    public static bool IsNotFound(this RequestFailedException requestFailedException)
    {
        return requestFailedException?.Status == (int)HttpStatusCode.NotFound;
    }

    public static bool IsPreconditionFailed(this RequestFailedException requestFailedException)
    {
        return requestFailedException?.Status == (int)HttpStatusCode.PreconditionFailed;
    }

    public static bool IsConflict(this RequestFailedException requestFailedException)
    {
        return requestFailedException?.Status == (int)HttpStatusCode.Conflict;
    }
}
