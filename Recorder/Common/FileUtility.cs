using System;
using System.IO;
using System.Threading.Tasks;

namespace DiscoPanda
{
    public class FileUtility
    {
        public static void WriteBytes(byte[] bytes, string path)
        {
            Task.Run(async () =>
            {
                await SaveByteArrayToFileAsync(bytes, path);
            });
        }

        static async Task SaveByteArrayToFileAsync(byte[] bytes, string path)
        {
            const int BufferSize = 65536;
            await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);

            // Write the bytes to the file
            await fileStream.WriteAsync(bytes.AsMemory(0, bytes.Length));
        }
    }
}