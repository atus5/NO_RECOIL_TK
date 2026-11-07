using System.Text;
using System.Xml;
using System.Xml.Serialization;
using HackTK.Next.Abstractions;

namespace HackTK.Next.Configuration;

public sealed class AtomicXmlConfigurationStore<T> : IConfigurationStore<T> where T : class, new()
{
    public const int MaxDocumentBytes = 65536;
    private static readonly XmlSerializer Serializer = new(typeof(T));

    public T Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return ReadOnce(fullPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                throw;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
            {
                Thread.Sleep(15);
            }
        }
    }

    public void Save(string path, T profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(profile);

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true
            }))
            {
                Serializer.Serialize(writer, profile);
            }

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(fullPath))
                        File.Replace(tempPath, fullPath, destinationBackupFileName: null);
                    else
                        File.Move(tempPath, fullPath);
                    break;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
                {
                    Thread.Sleep(15);
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Do not mask primary exception if temporary file cleanup encounters a transient lock.
            }
        }
    }

    private static T ReadOnce(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > MaxDocumentBytes)
            throw new InvalidDataException($"Tệp cấu hình vượt giới hạn {MaxDocumentBytes} byte.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxDocumentBytes
        };

        try
        {
            using var reader = XmlReader.Create(input, settings);
            object? deserialized = Serializer.Deserialize(reader);
            return deserialized as T ?? throw new InvalidDataException("Không thể giải mã tệp cấu hình XML.");
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new InvalidDataException($"Tệp cấu hình XML không hợp lệ: {ex.Message}", ex);
        }
    }
}
