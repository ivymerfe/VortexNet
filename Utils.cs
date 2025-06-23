using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VortexNet
{
    public static class Utils
    {
        public static void CreateDirectoryRecursive(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            Directory.CreateDirectory(path);
        }

        public static async Task<bool> DownloadFileAsync(string url, string filePath, int maxRetries = 1)
        {
            int retries = 0;

            while (retries < maxRetries)
            {
                try
                {
                    // Create directory structure
                    Utils.CreateDirectoryRecursive(Path.GetDirectoryName(filePath));

                    // Download file
                    using HttpClient client = new();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    byte[] data = await client.GetByteArrayAsync(url);

                    using FileStream fileStream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await fileStream.WriteAsync(data);

                    return true;
                }
                catch
                {
                    retries++;

                    if (retries >= maxRetries)
                        return false;

                    await Task.Delay(500);
                }
            }

            return false;
        }

        public static string RemoveSpacesFromVersionName(string version)
        {
            string newVersion = version.Replace(" ", "-");
            string versionDir = Path.Combine("versions", version);
            string newVersionDir = Path.Combine("versions", newVersion);

            // Rename files and directory
            if (File.Exists(Path.Combine(versionDir, $"{version}.jar")))
            {
                File.Move(Path.Combine(versionDir, $"{version}.jar"),
                          Path.Combine(versionDir, $"{newVersion}.jar"));
            }

            if (File.Exists(Path.Combine(versionDir, $"{version}.json")))
            {
                File.Move(Path.Combine(versionDir, $"{version}.json"),
                          Path.Combine(versionDir, $"{newVersion}.json"));
            }

            Directory.Move(versionDir, newVersionDir);

            return newVersion;
        }
        public static void ExtractNatives(string zipPath, string outputPath)
        {
            try
            {
                if (!File.Exists(zipPath))
                    return;

                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith(".dll") || entry.FullName.EndsWith(".so"))
                    {
                        string outputFilePath = Path.Combine(outputPath, entry.Name);

                        if (!File.Exists(outputFilePath) || new FileInfo(outputFilePath).Length < 1)
                        {
                            entry.ExtractToFile(outputFilePath, true);
                        }
                    }
                }
            }
            catch
            {
                // Error handling
            }
        }

        public static void AssetsToResources(string assetsIndex)
        {
            string indexPath = Path.Combine("assets", "indexes", $"{assetsIndex}.json");

            if (!File.Exists(indexPath))
                return;

            try
            {
                string assetsJson = File.ReadAllText(indexPath);

                using JsonDocument doc = JsonDocument.Parse(assetsJson);
                JsonElement root = doc.RootElement;
                JsonElement objects = root.GetProperty("objects");

                foreach (JsonProperty asset in objects.EnumerateObject())
                {
                    string fileName = asset.Name;
                    string hash = asset.Value.GetProperty("hash").GetString();
                    int size = asset.Value.GetProperty("size").GetInt32();

                    // Create path
                    string resourcePath = Path.Combine("resources", fileName.Replace("/", "\\"));
                    string hashPath = Path.Combine("assets", "objects", hash.Substring(0, 2), hash);

                    // Check if we need to copy
                    bool needCopy = !File.Exists(resourcePath) || new FileInfo(resourcePath).Length != size;

                    if (needCopy && File.Exists(hashPath))
                    {
                        // Create directory structure
                        Directory.CreateDirectory(Path.GetDirectoryName(resourcePath));

                        // Copy file
                        File.Copy(hashPath, resourcePath, true);
                    }
                }
            }
            catch
            {
                // Error handling
            }
        }

        public static string GenerateOfflineUUID(string username)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes("OfflinePlayer:" + username);
            byte[] hashBytes = MD5.HashData(inputBytes);

            // Convert to UUID format
            hashBytes[6] = (byte)((hashBytes[6] & 0x0F) | 0x30);
            hashBytes[8] = (byte)((hashBytes[8] & 0x3F) | 0x80);

            // Format UUID with dashes (8-4-4-4-12)
            return $"{BitConverter.ToString(hashBytes, 0, 4).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(hashBytes, 4, 2).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(hashBytes, 6, 2).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(hashBytes, 8, 2).Replace("-", "").ToLower()}-" +
                   $"{BitConverter.ToString(hashBytes, 10, 6).Replace("-", "").ToLower()}";
        }
    }
}
