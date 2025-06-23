using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VortexNet
{
    public partial class MainWindow : Window
    {
        private string workingDirectory;
        private string tempDirectory;
        private readonly List<string> programFilesDirs = [];
        private readonly List<string> javaDirs = [];

        private CancellationTokenSource downloadCancellationToken;
        private int downloadThreadsAmount = 20;
        private bool asyncDownload = true;
        private bool forceDownloadMissingLibraries = false;

        private string versionsManifestString;
        private string playerName = "Player";
        private string ramAmount = "2500";
        private string javaBinaryPath;
        private string customLaunchArguments = "-Xss1M -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M";
        private string customOldLaunchArguments = "-XX:+UseConcMarkSweepGC -XX:+CMSIncrementalMode -XX:-UseAdaptiveSizePolicy -Xmn128M";

        private bool downloadMissingLibraries = true;
        private bool saveLaunchStringDefault = false;
        private bool useCustomParamsDefault = false;
        private bool keepLauncherOpenDefault = false;
        private bool useCustomJavaDefault = false;
        private string javaBinaryPathDefault = @"C:\jre8\bin\javaw.exe";
        private bool isInitializing = true;

        private LauncherSettings settings;
        private string settingsPath;

        public MainWindow()
        {
            InitializeComponent();
            settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vortex_settings.json");
            InitializeLauncher();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void InitializeLauncher()
        {
            // Set working and temp directories
            workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            tempDirectory = Path.GetTempPath();

            // Initialize program files and Java directories
            programFilesDirs.Add(Environment.GetEnvironmentVariable("PROGRAMFILES") + "\\");
            programFilesDirs.Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)") + "\\");

            javaDirs.Add("Java");
            javaDirs.Add("Eclipse Adoptium");

            // Set the working directory
            Directory.SetCurrentDirectory(workingDirectory);

            // Load preferences
            LoadSettings();

            // Find versions and Java
            FindInstalledVersions();
            FindJava();

            // Delete download list file if exists
            File.Delete(Path.Combine(tempDirectory, "minecraft_download_list.txt"));

            // Remove Java environment variable that might interfere
            Environment.SetEnvironmentVariable("_JAVA_OPTIONS", null);

            // Generate launcher profiles
            GenerateProfileJson();

            // Fetch versions asynchronously
            _ = FetchVersionsManifest();

            isInitializing = false;
        }

        private void LoadSettings()
        {
            settings = new LauncherSettings();

            if (File.Exists(settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(settingsPath);
                    settings = JsonSerializer.Deserialize<LauncherSettings>(json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading settings: {ex.Message}", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // Apply settings to variables
            downloadThreadsAmount = settings.DownloadThreads;
            asyncDownload = settings.AsyncDownload;
            playerName = settings.PlayerName;
            ramAmount = settings.RamAmount;
            downloadMissingLibraries = settings.DownloadMissingLibs;
            useCustomJavaDefault = settings.UseCustomJava;
            javaBinaryPathDefault = settings.CustomJavaPath;
            customLaunchArguments = settings.LaunchArguments;
            useCustomParamsDefault = settings.UseCustomParameters;
            keepLauncherOpenDefault = settings.KeepLauncherOpen;
            saveLaunchStringDefault = settings.SaveLaunchString;

            // Update UI controls
            playerNameTextBox.Text = playerName;
            ramAmountTextBox.Text = ramAmount;
            downloadThreadsTextBox.Text = downloadThreadsAmount.ToString();
            asyncDownloadCheckBox.IsChecked = asyncDownload;
            downloadMissingLibsCheckBox.IsChecked = downloadMissingLibraries;
            useCustomJavaCheckBox.IsChecked = useCustomJavaDefault;
            javaPathTextBox.Text = javaBinaryPathDefault;
            useCustomParamsCheckBox.IsChecked = useCustomParamsDefault;
            launchArgsTextBox.Text = customLaunchArguments;
            keepLauncherOpenCheckBox.IsChecked = keepLauncherOpenDefault;
            saveLaunchStringCheckBox.IsChecked = saveLaunchStringDefault;
            showAllVersionsCheckBox.IsChecked = settings.ShowAllVersions;
        }

        private void SaveSettings()
        {
            if (isInitializing)
                return;

            try
            {
                settings.PlayerName = playerNameTextBox.Text;
                settings.RamAmount = ramAmountTextBox.Text;
                settings.DownloadThreads = int.Parse(downloadThreadsTextBox.Text);
                settings.AsyncDownload = asyncDownloadCheckBox.IsChecked ?? true;
                settings.DownloadMissingLibs = downloadMissingLibsCheckBox.IsChecked ?? true;
                settings.UseCustomJava = useCustomJavaCheckBox.IsChecked ?? false;
                settings.CustomJavaPath = javaPathTextBox.Text;
                settings.LaunchArguments = launchArgsTextBox.Text;
                settings.UseCustomParameters = useCustomParamsCheckBox.IsChecked ?? false;
                settings.KeepLauncherOpen = keepLauncherOpenCheckBox.IsChecked ?? true;
                settings.SaveLaunchString = saveLaunchStringCheckBox.IsChecked ?? false;
                settings.ShowAllVersions = showAllVersionsCheckBox.IsChecked ?? false;

                if (versionsComboBox.SelectedItem != null)
                {
                    settings.ChosenVersion = versionsComboBox.SelectedItem.ToString();
                }

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);

                // Update runtime variables
                playerName = playerNameTextBox.Text;
                ramAmount = ramAmountTextBox.Text;
                downloadThreadsAmount = int.Parse(downloadThreadsTextBox.Text);
                asyncDownload = asyncDownloadCheckBox.IsChecked ?? true;
                downloadMissingLibraries = downloadMissingLibsCheckBox.IsChecked ?? true;
                customLaunchArguments = launchArgsTextBox.Text;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FindInstalledVersions()
        {
            versionsComboBox.Items.Clear();

            string chosenVersion = settings.ChosenVersion;
            bool chosenFound = false;

            string versionsDir = Path.Combine(workingDirectory, "versions");
            if (!Directory.Exists(versionsDir))
            {
                Directory.CreateDirectory(versionsDir);
            }

            foreach (string directory in Directory.GetDirectories(versionsDir))
            {
                string dirName = new DirectoryInfo(directory).Name;
                if (File.Exists(Path.Combine(directory, $"{dirName}.json")))
                {
                    versionsComboBox.Items.Add(dirName);

                    if (dirName == chosenVersion)
                    {
                        chosenFound = true;
                    }
                }
            }

            if (versionsComboBox.Items.Count > 0)
            {
                if (chosenFound)
                {
                    versionsComboBox.SelectedItem = chosenVersion;
                }
                else
                {
                    versionsComboBox.SelectedIndex = 0;
                }

                playButton.IsEnabled = true;
            }
            else
            {
                versionsComboBox.Items.Add("Versions not found");
                versionsComboBox.SelectedIndex = 0;
                versionsComboBox.IsEnabled = false;
                playButton.IsEnabled = false;
            }
        }

        private void FindJava()
        {
            javaComboBox.Items.Clear();
            javaComboBox.IsEnabled = true;

            if (useCustomJavaCheckBox.IsChecked == true)
            {
                javaComboBox.Items.Add("Custom Java");
                javaComboBox.SelectedIndex = 0;
                javaComboBox.IsEnabled = false;
                return;
            }

            // Find Java installations
            foreach (string programFilesDir in programFilesDirs)
            {
                if (programFilesDir == "\\") continue;

                foreach (string javaDir in javaDirs)
                {
                    string javaDirPath = Path.Combine(programFilesDir, javaDir);
                    if (!Directory.Exists(javaDirPath)) continue;

                    foreach (string directory in Directory.GetDirectories(javaDirPath))
                    {
                        string dirName = new DirectoryInfo(directory).Name;
                        string javaPath = Path.Combine(directory, "bin", "javaw.exe");

                        if (File.Exists(javaPath))
                        {
                            if (programFilesDir == programFilesDirs[1]) // x86 Program Files
                            {
                                javaComboBox.Items.Add($"{dirName} (x32)");
                            }
                            else
                            {
                                javaComboBox.Items.Add(dirName);
                            }
                        }
                    }
                }
            }

            if (javaComboBox.Items.Count > 0)
            {
                javaComboBox.SelectedIndex = 0;
                playButton.IsEnabled = versionsComboBox.Items.Count > 0 &&
                                    versionsComboBox.SelectedItem.ToString() != "Versions not found";
            }
            else
            {
                javaComboBox.Items.Add("Java not found");
                javaComboBox.SelectedIndex = 0;
                javaComboBox.IsEnabled = false;
                playButton.IsEnabled = false;
            }
        }

        private void GenerateProfileJson()
        {
            string fileName = "launcher_profiles.json";
            string filePath = Path.Combine(workingDirectory, fileName);

            if (!File.Exists(filePath) || new FileInfo(filePath).Length <= 0)
            {
                using (StreamWriter file = File.CreateText(filePath))
                {
                    file.Write("{ \"profiles\": { \"justProfile\": { \"name\": \"justProfile\", \"lastVersionId\": \"1.12.2\" } } }");
                }
                forceDownloadMissingLibraries = true;
            }

            int fileSize = (int)new FileInfo(filePath).Length;
            if (fileSize != settings.LastProfilesJsonSize)
            {
                forceDownloadMissingLibraries = true;
            }

            settings.LastProfilesJsonSize = fileSize;
            SaveSettings();
        }

        private async Task FetchVersionsManifest()
        {
            try
            {
                using var client = new HttpClient();
                var response = await client.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest.json");
                versionsManifestString = response;

                // We need to use Dispatcher since this is being called from an async method
                Dispatcher.Invoke(() =>
                {
                    ParseVersionsManifest(showAllVersionsCheckBox.IsChecked ?? false);
                });
            }
            catch
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show("Failed to fetch Minecraft versions. Check your internet connection.",
                        "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }

        private void ParseVersionsManifest(bool showAllVersions = false)
        {
            if (string.IsNullOrEmpty(versionsManifestString))
                return;

            try
            {
                downloadVersionsComboBox.Items.Clear();

                using (JsonDocument doc = JsonDocument.Parse(versionsManifestString))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement versions = root.GetProperty("versions");

                    foreach (JsonElement version in versions.EnumerateArray())
                    {
                        string id = version.GetProperty("id").GetString();
                        string type = version.GetProperty("type").GetString();

                        if (!showAllVersions && type != "release")
                        {
                            continue;
                        }

                        downloadVersionsComboBox.Items.Add(id);
                    }
                }

                if (downloadVersionsComboBox.Items.Count > 0)
                {
                    downloadVersionsComboBox.SelectedIndex = 0;
                    downloadVersionButton.IsEnabled = true;
                }
                else
                {
                    downloadVersionsComboBox.Items.Add("Error");
                    downloadVersionButton.IsEnabled = false;
                }
            }
            catch
            {
                downloadVersionsComboBox.Items.Add("Error parsing versions");
                downloadVersionButton.IsEnabled = false;
            }
        }

        private string GetVersionManifestUrl(string clientVersion)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(versionsManifestString);
                JsonElement root = doc.RootElement;
                JsonElement versions = root.GetProperty("versions");

                foreach (JsonElement version in versions.EnumerateArray())
                {
                    string id = version.GetProperty("id").GetString();
                    string url = version.GetProperty("url").GetString();

                    if (id == clientVersion)
                    {
                        return url;
                    }
                }
            }
            catch { }

            return "";
        }

        private async void DownloadVersionButton_Click(object sender, RoutedEventArgs e)
        {
            string versionToDownload = downloadVersionsComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(versionToDownload) || versionToDownload == "Error")
                return;

            // Create version directory
            Utils.CreateDirectoryRecursive(Path.Combine("versions", versionToDownload));

            // Get version JSON URL
            string jsonUrl = GetVersionManifestUrl(versionToDownload);
            if (string.IsNullOrEmpty(jsonUrl))
            {
                MessageBox.Show("Failed to get version information.", "Download Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Download version JSON
            string jsonPath = Path.Combine("versions", versionToDownload, $"{versionToDownload}.json");
            bool jsonDownloaded = await Utils.DownloadFileAsync(jsonUrl, jsonPath);

            if (!jsonDownloaded)
            {
                MessageBox.Show("Failed to download version information.", "Download Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Prepare download list
            string downloadListPath = Path.Combine(tempDirectory, "minecraft_download_list.txt");
            File.Delete(downloadListPath);

            using (StreamWriter listFile = new(downloadListPath))
            {
                // Parse version JSON for downloads
                string versionJson = File.ReadAllText(jsonPath);

                using JsonDocument doc = JsonDocument.Parse(versionJson);
                JsonElement root = doc.RootElement;

                // Asset index
                string assetsIndex = root.GetProperty("assets").GetString();
                string assetIndexUrl = root.GetProperty("assetIndex").GetProperty("url").GetString();

                // Create assets directory
                Utils.CreateDirectoryRecursive(Path.Combine("assets", "indexes"));

                // Download asset index
                string assetIndexPath = Path.Combine("assets", "indexes", $"{assetsIndex}.json");
                await Utils.DownloadFileAsync(assetIndexUrl, assetIndexPath);

                // Check for logging config
                if (root.TryGetProperty("logging", out JsonElement logging))
                {
                    if (logging.TryGetProperty("client", out JsonElement client))
                    {
                        if (client.TryGetProperty("file", out JsonElement file))
                        {
                            string logConfId = file.GetProperty("id").GetString();
                            string logConfUrl = file.GetProperty("url").GetString();
                            int logConfSize = file.GetProperty("size").GetInt32();

                            Utils.CreateDirectoryRecursive(Path.Combine("assets", "log_configs"));
                            listFile.WriteLine($"{logConfUrl}::assets\\log_configs\\{logConfId}::{logConfSize}");
                        }
                    }
                }

                // Client jar
                string clientUrl = root.GetProperty("downloads").GetProperty("client").GetProperty("url").GetString();
                int clientSize = root.GetProperty("downloads").GetProperty("client").GetProperty("size").GetInt32();

                listFile.WriteLine($"{clientUrl}::versions\\{versionToDownload}\\{versionToDownload}.jar::{clientSize}");

                // Libraries
                ParseLibrariesForDownload(versionJson, versionToDownload, listFile);

                // Parse assets
                string assetsJson = File.ReadAllText(Path.Combine("assets", "indexes", $"{assetsIndex}.json"));

                using JsonDocument assetsDoc = JsonDocument.Parse(assetsJson);
                JsonElement assetsRoot = assetsDoc.RootElement;
                JsonElement objects = assetsRoot.GetProperty("objects");

                foreach (JsonProperty asset in objects.EnumerateObject())
                {
                    string hash = asset.Value.GetProperty("hash").GetString();
                    int size = asset.Value.GetProperty("size").GetInt32();

                    string hashPrefix = hash.Substring(0, 2);
                    listFile.WriteLine($"https://resources.download.minecraft.net/{hashPrefix}/{hash}::assets\\objects\\{hashPrefix}\\{hash}::{size}");
                }
            }

            // Start download
            progressPanel.Visibility = Visibility.Visible;
            playButton.IsEnabled = false;
            downloadVersionButton.IsEnabled = false;
            downloadProgressBar.Visibility = Visibility.Visible;

            // Update progress information
            downloadingVersionLabel.Text = $"Version: {versionToDownload}";
            filesLeftLabel.Text = "Preparing to download...";

            // Start download process
            downloadCancellationToken = new CancellationTokenSource();
            bool redownloadAllFiles = redownloadAllFilesCheckBox.IsChecked ?? false;
            bool success = await Task.Run(() => DownloadFiles(redownloadAllFiles, downloadCancellationToken.Token));

            if (!success)
            {
                filesLeftLabel.Text = "Download failed!";
            }
            else
            {
                filesLeftLabel.Text = "Download complete!";
            }

            downloadProgressBar.Visibility = Visibility.Collapsed;
            playButton.IsEnabled = true;
            downloadVersionButton.IsEnabled = true;

            // Update versions list
            FindInstalledVersions();
        }

        private bool ParseLibrariesForDownload(string versionJson, string versionName, StreamWriter listFile)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(versionJson);
                JsonElement root = doc.RootElement;
                JsonElement libraries = root.GetProperty("libraries");

                foreach (JsonElement library in libraries.EnumerateArray())
                {
                    bool allowLib = true;

                    // Check rules
                    if (library.TryGetProperty("rules", out JsonElement rules))
                    {
                        foreach (JsonElement rule in rules.EnumerateArray())
                        {
                            string action = rule.GetProperty("action").GetString();

                            if (rule.TryGetProperty("os", out JsonElement os))
                            {
                                string osName = os.GetProperty("name").GetString();

                                if (action == "allow")
                                {
                                    if (osName != "windows")
                                    {
                                        allowLib = false;
                                    }
                                }
                                else // disallow
                                {
                                    if (osName == "windows")
                                    {
                                        allowLib = false;
                                    }
                                }
                            }
                        }
                    }

                    if (!allowLib) continue;

                    string name = library.GetProperty("name").GetString();
                    string[] nameParts = name.Split(':');

                    if (nameParts.Length < 3) continue;

                    // Build library path
                    string domain = nameParts[0].Replace(".", "\\");
                    string artifact = nameParts[1];
                    string version = nameParts[2];
                    string classifier = nameParts.Length > 3 ? nameParts[3] : "";

                    string libBasePath = $"{domain}\\{artifact}";
                    string libPath = $"{libBasePath}\\{version}\\{artifact}-{version}";

                    if (!string.IsNullOrEmpty(classifier))
                    {
                        libPath += $"-{classifier}";
                    }

                    // Check for downloads
                    string url = "";
                    int fileSize = 0;

                    if (library.TryGetProperty("downloads", out JsonElement downloads))
                    {
                        // Check for artifact
                        if (downloads.TryGetProperty("artifact", out JsonElement artifact2))
                        {
                            url = artifact2.GetProperty("url").GetString();
                            fileSize = artifact2.GetProperty("size").GetInt32();
                        }
                        // Check for classifier (natives)
                        else if (downloads.TryGetProperty("classifiers", out JsonElement classifiers))
                        {
                            if (classifiers.TryGetProperty("natives-windows", out JsonElement nativesWindows))
                            {
                                url = nativesWindows.GetProperty("url").GetString();
                                fileSize = nativesWindows.GetProperty("size").GetInt32();
                                libPath += "-natives-windows";
                            }
                        }
                    }
                    else if (library.TryGetProperty("url", out JsonElement urlElem))
                    {
                        url = urlElem.GetString() + libPath.Replace("\\", "/") + ".jar";
                    }
                    else
                    {
                        url = $"https://libraries.minecraft.net/{libPath.Replace("\\", "/")}.jar";
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        listFile.WriteLine($"{url}::libraries\\{libPath}.jar::{fileSize}");
                    }

                    // Extract natives if needed
                    if (library.TryGetProperty("natives", out JsonElement _))
                    {
                        Utils.CreateDirectoryRecursive(Path.Combine("versions", versionName, "natives"));
                        // Native extraction will be done during launch
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> DownloadFiles(bool downloadAllFiles, CancellationToken token)
        {
            string downloadListPath = Path.Combine(tempDirectory, "minecraft_download_list.txt");
            if (!File.Exists(downloadListPath))
                return false;

            string[] lines = File.ReadAllLines(downloadListPath);
            int linesTotal = lines.Length;
            int linesLeft = linesTotal;
            int succeeded = 0;
            int failed = 0;

            // Update UI
            Dispatcher.Invoke(() =>
            {
                downloadProgressBar.Maximum = linesTotal;
                downloadProgressBar.Value = 0;
                filesLeftLabel.Text = $"Files remaining: {linesLeft}";
            });

            if (asyncDownload)
            {
                // Create tasks list
                var downloadTasks = new List<Task>();
                var semaphore = new SemaphoreSlim(downloadThreadsAmount);

                foreach (string line in lines)
                {
                    if (token.IsCancellationRequested)
                        return false;

                    string[] parts = line.Split(["::"], StringSplitOptions.None);
                    if (parts.Length < 2)
                        continue;

                    string url = parts[0];
                    string filePath = parts[1];
                    int requiredSize = parts.Length > 2 ? int.Parse(parts[2]) : 0;

                    FileInfo fileInfo = new(filePath);
                    bool needDownload = downloadAllFiles ||
                                       !fileInfo.Exists ||
                                       (requiredSize > 0 && fileInfo.Length != requiredSize);

                    if (!needDownload)
                    {
                        linesLeft--;
                        succeeded++;

                        Dispatcher.Invoke(() =>
                        {
                            downloadProgressBar.Value = linesTotal - linesLeft;
                            filesLeftLabel.Text = $"Files remaining: {linesLeft}";
                        });

                        continue;
                    }

                    await semaphore.WaitAsync(token);

                    downloadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            Utils.CreateDirectoryRecursive(Path.GetDirectoryName(filePath));
                            bool success = await Utils.DownloadFileAsync(url, filePath, 5);

                            if (success)
                                Interlocked.Increment(ref succeeded);
                            else
                                Interlocked.Increment(ref failed);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref linesLeft);
                            semaphore.Release();

                            Dispatcher.Invoke(() =>
                            {
                                downloadProgressBar.Value = linesTotal - linesLeft;
                                filesLeftLabel.Text = $"Files remaining: {linesLeft}";
                            });
                        }
                    }, token));
                }

                try
                {
                    await Task.WhenAll(downloadTasks);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
            else
            {
                // Sequential downloads
                foreach (string line in lines)
                {
                    if (token.IsCancellationRequested)
                        return false;

                    string[] parts = line.Split(["::"], StringSplitOptions.None);
                    if (parts.Length < 2)
                        continue;

                    string url = parts[0];
                    string filePath = parts[1];
                    int requiredSize = parts.Length > 2 ? int.Parse(parts[2]) : 0;

                    FileInfo fileInfo = new(filePath);
                    bool needDownload = downloadAllFiles ||
                                       !fileInfo.Exists ||
                                       (requiredSize > 0 && fileInfo.Length != requiredSize);

                    if (!needDownload)
                    {
                        linesLeft--;
                        succeeded++;

                        Dispatcher.Invoke(() =>
                        {
                            downloadProgressBar.Value = linesTotal - linesLeft;
                            filesLeftLabel.Text = $"Files remaining: {linesLeft}";
                        });

                        continue;
                    }

                    // Create directory if doesn't exist
                    Utils.CreateDirectoryRecursive(Path.GetDirectoryName(filePath));

                    // Download file
                    bool success = await Utils.DownloadFileAsync(url, filePath, 5);

                    if (success)
                        succeeded++;
                    else
                        failed++;

                    linesLeft--;

                    Dispatcher.Invoke(() =>
                    {
                        downloadProgressBar.Value = linesTotal - linesLeft;
                        filesLeftLabel.Text = $"Files remaining: {linesLeft}";
                    });
                }
            }

            return failed <= 5;
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string selectedVersion = versionsComboBox.SelectedItem?.ToString();
                playerName = playerNameTextBox.Text;
                ramAmount = ramAmountTextBox.Text;

                // Validate inputs
                if (string.IsNullOrEmpty(selectedVersion) || selectedVersion == "Versions not found")
                {
                    MessageBox.Show("Please select a valid Minecraft version.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Remove spaces from name
                playerName = playerName.Replace(" ", "");

                if (string.IsNullOrEmpty(playerName) || playerName.Length < 3)
                {
                    MessageBox.Show("Enter your desired name. Minimum length is 3.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrEmpty(ramAmount))
                {
                    MessageBox.Show("Enter RAM amount.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Check if RAM amount is too low
                if (!int.TryParse(ramAmount, out int ramValue))
                {
                    MessageBox.Show("Invalid RAM amount.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (ramValue < 600)
                {
                    ramAmount = "600";
                    ramAmountTextBox.Text = ramAmount;
                    MessageBox.Show("You allocated too low amount of memory!\n\nAllocated memory set to 600 MB to prevent crashes.",
                        "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Replace version name if it contains spaces
                if (selectedVersion.Contains(' '))
                {
                    selectedVersion = Utils.RemoveSpacesFromVersionName(selectedVersion);
                }

                // Set Java binary path
                if (useCustomJavaCheckBox.IsChecked == true)
                {
                    javaBinaryPath = javaPathTextBox.Text;
                }
                else
                {
                    javaBinaryPath = GetJavaBinaryPath(javaComboBox.SelectedItem.ToString());
                }

                // Check if Java exists
                if (!File.Exists(javaBinaryPath))
                {
                    MessageBox.Show("Java not found! Check if Java is installed or if the path to Java binary is correct.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Save preferences
                settings.PlayerName = playerName;
                settings.RamAmount = ramAmount;
                settings.ChosenVersion = selectedVersion;
                SaveSettings();

                // Launch Minecraft
                LaunchMinecraft(selectedVersion);

                // Optionally close launcher
                if (!(keepLauncherOpenCheckBox.IsChecked ?? true))
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error launching Minecraft: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetJavaBinaryPath(string javaSelection)
        {
            if (javaSelection.EndsWith(" (x32)"))
            {
                string javaVersion = javaSelection.Replace(" (x32)", "");

                foreach (string javaDir in javaDirs)
                {
                    string path = Path.Combine(programFilesDirs[1], javaDir, javaVersion, "bin", "javaw.exe");
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            else if (javaSelection != "Java not found" && javaSelection != "Custom Java enabled in Settings")
            {
                foreach (string javaDir in javaDirs)
                {
                    string path = Path.Combine(programFilesDirs[0], javaDir, javaSelection, "bin", "javaw.exe");
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }

            return javaBinaryPathDefault;
        }

        private void LaunchMinecraft(string version)
        {
            string clientJarFile = Path.Combine("versions", version, $"{version}.jar");
            string nativesPath = Path.Combine("versions", version, "natives");
            string versionJsonPath = Path.Combine("versions", version, $"{version}.json");

            if (!File.Exists(versionJsonPath))
            {
                MessageBox.Show("Client json file is missing!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Parse version JSON
            string versionJson = File.ReadAllText(versionJsonPath);
            JsonDocument jsonDoc = JsonDocument.Parse(versionJson);
            JsonElement root = jsonDoc.RootElement;

            string inheritsFrom = "";
            string assetsIndex = "legacy";
            string clientArguments = "";
            string jvmArguments = "";
            string logConfArgument = "";
            string librariesString = "";

            // Check for jar and inheritsFrom
            if (root.TryGetProperty("jar", out JsonElement jarElem))
            {
                inheritsFrom = jarElem.GetString();
            }

            if (root.TryGetProperty("inheritsFrom", out JsonElement inheritsElem))
            {
                inheritsFrom = inheritsElem.GetString();
            }

            // Handle inheritance
            if (!string.IsNullOrEmpty(inheritsFrom))
            {
                string inheritsClientJar = inheritsFrom;

                if (new FileInfo(clientJarFile).Length < 1 &&
                    File.Exists(Path.Combine("versions", inheritsClientJar, $"{inheritsClientJar}.jar")))
                {
                    File.Copy(Path.Combine("versions", inheritsClientJar, $"{inheritsClientJar}.jar"),
                              clientJarFile);
                }

                nativesPath = Path.Combine("versions", inheritsClientJar, "natives");

                // Parse inherits JSON
                string inheritsJsonPath = Path.Combine("versions", inheritsClientJar, $"{inheritsClientJar}.json");

                if (File.Exists(inheritsJsonPath))
                {
                    string inheritsJson = File.ReadAllText(inheritsJsonPath);

                    using JsonDocument inheritsDoc = JsonDocument.Parse(inheritsJson);
                    JsonElement inheritsRoot = inheritsDoc.RootElement;

                    // Get assets index from inheritsFrom
                    assetsIndex = inheritsRoot.GetProperty("assets").GetString();

                    // Get arguments
                    if (inheritsRoot.TryGetProperty("arguments", out JsonElement arguments))
                    {
                        if (arguments.TryGetProperty("game", out JsonElement gameArgs))
                        {
                            foreach (JsonElement arg in gameArgs.EnumerateArray())
                            {
                                if (arg.ValueKind == JsonValueKind.String)
                                {
                                    clientArguments += " " + arg.GetString() + " ";
                                }
                            }
                        }

                        if (arguments.TryGetProperty("jvm", out JsonElement jvmArgs))
                        {
                            foreach (JsonElement arg in jvmArgs.EnumerateArray())
                            {
                                if (arg.ValueKind == JsonValueKind.String)
                                {
                                    jvmArguments += " \"" + arg.GetString() + "\" ";
                                }
                            }
                        }
                    }

                    // Parse libraries from inheritsFrom
                    librariesString += ParseLibrariesForLaunch(inheritsClientJar, downloadMissingLibraries);

                    // Get logging configuration
                    if (inheritsRoot.TryGetProperty("logging", out JsonElement logging2))
                    {
                        if (logging2.TryGetProperty("client", out JsonElement client))
                        {
                            if (client.TryGetProperty("file", out JsonElement file))
                            {
                                string logConfId = file.GetProperty("id").GetString();
                                logConfArgument = $"-Dlog4j.configurationFile=assets\\log_configs\\{logConfId}";
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"{inheritsClientJar}.json file is missing!", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                // Get assets index from current version
                if (root.TryGetProperty("assets", out JsonElement assetsElem))
                {
                    assetsIndex = assetsElem.GetString();
                }
                else if (root.TryGetProperty("releaseTime", out JsonElement releaseTime))
                {
                    string releaseTimeStr = releaseTime.GetString();
                    string[] parts = releaseTimeStr.Split('-');
                    int releaseTimeValue = int.Parse(parts[0]) * 365 + int.Parse(parts[1]) * 30;

                    if (releaseTimeValue < 734925)
                    {
                        assetsIndex = "pre-1.6";
                    }
                }
            }

            // Get arguments from current version
            if (root.TryGetProperty("minecraftArguments", out JsonElement minecraftArgs))
            {
                clientArguments = minecraftArgs.GetString();
            }
            else if (root.TryGetProperty("arguments", out JsonElement arguments))
            {
                if (arguments.TryGetProperty("game", out JsonElement gameArgs))
                {
                    foreach (JsonElement arg in gameArgs.EnumerateArray())
                    {
                        if (arg.ValueKind == JsonValueKind.String)
                        {
                            clientArguments += " " + arg.GetString() + " ";
                        }
                    }
                }

                if (arguments.TryGetProperty("jvm", out JsonElement jvmArgs))
                {
                    foreach (JsonElement arg in jvmArgs.EnumerateArray())
                    {
                        if (arg.ValueKind == JsonValueKind.String)
                        {
                            jvmArguments += " \"" + arg.GetString() + "\" ";
                        }
                    }
                }
            }

            // Get logging configuration from current version
            if (root.TryGetProperty("logging", out JsonElement logging) && string.IsNullOrEmpty(logConfArgument))
            {
                if (logging.TryGetProperty("client", out JsonElement client))
                {
                    if (client.TryGetProperty("file", out JsonElement file))
                    {
                        string logConfId = file.GetProperty("id").GetString();
                        logConfArgument = $"-Dlog4j.configurationFile=assets/log_configs/{logConfId}";
                    }
                }
            }

            // Get main class
            string clientMainClass = root.GetProperty("mainClass").GetString();

            // Parse libraries from current version
            librariesString += ParseLibrariesForLaunch(version, downloadMissingLibraries);

            // Check if client jar exists
            if (File.Exists(clientJarFile))
            {
                // If assets need to be copied to resources
                if (assetsIndex == "pre-1.6" || assetsIndex == "legacy")
                {
                    Utils.AssetsToResources(assetsIndex);
                }

                // Download missing libraries if needed
                if (downloadMissingLibraries || forceDownloadMissingLibraries)
                {
                    // This would trigger library download if needed
                }

                // Set JVM arguments if not set yet
                if (string.IsNullOrEmpty(jvmArguments))
                {
                    jvmArguments = $"\"-Djava.library.path={nativesPath}\" -cp \"{librariesString}{clientJarFile}\"";
                }

                // Determine appropriate launch arguments based on version age
                string customArgs;
                if (root.TryGetProperty("releaseTime", out JsonElement releaseTimeElem))
                {
                    string releaseTimeStr = releaseTimeElem.GetString();
                    string[] parts = releaseTimeStr.Split('-');
                    int releaseTimeValue = int.Parse(parts[0]) * 365 + int.Parse(parts[1]) * 30;

                    if (releaseTimeValue < 736780)
                    {
                        customArgs = customOldLaunchArguments;
                    }
                    else
                    {
                        customArgs = customLaunchArguments;
                    }
                }
                else
                {
                    customArgs = customLaunchArguments;
                }

                // Use custom parameters if enabled
                if (useCustomParamsCheckBox.IsChecked == true)
                {
                    customArgs = launchArgsTextBox.Text;
                }

                // Generate UUID for offline mode
                string uuid = Utils.GenerateOfflineUUID(playerName);

                // Check if the arguments are in the new format 
                bool isNewFormat = clientArguments.Contains("--username") || clientArguments.Contains("${auth_player_name}");

                // Build full launch string
                string fullLaunchString = $"-Xmx{ramAmount}M {customArgs} -Dlog4j2.formatMsgNoLookups=true {logConfArgument} {jvmArguments} {clientMainClass}";

                // Add client arguments depending on format
                if (isNewFormat)
                {
                    fullLaunchString += " " + clientArguments;
                }
                else
                {
                    // For older versions use different format
                    fullLaunchString += $" --username {playerName} --version {version} --gameDir \"{workingDirectory}\" --assetsDir assets --assetIndex {assetsIndex} --uuid {uuid} --accessToken '00000000000000000000000000000000' --userType mojang";
                }

                // Replace variables in launch string
                fullLaunchString = fullLaunchString.Replace("${auth_player_name}", playerName);
                fullLaunchString = fullLaunchString.Replace("${version_name}", version);
                fullLaunchString = fullLaunchString.Replace("${game_directory}", $"\"{workingDirectory}\"");
                fullLaunchString = fullLaunchString.Replace("${assets_root}", "assets");
                fullLaunchString = fullLaunchString.Replace("${auth_uuid}", uuid);
                fullLaunchString = fullLaunchString.Replace("${auth_access_token}", "00000000000000000000000000000000");
                fullLaunchString = fullLaunchString.Replace("${clientid}", "0000");
                fullLaunchString = fullLaunchString.Replace("${auth_xuid}", "0000");
                fullLaunchString = fullLaunchString.Replace("${user_properties}", "{}");
                fullLaunchString = fullLaunchString.Replace("${user_type}", "mojang");
                fullLaunchString = fullLaunchString.Replace("${version_type}", "release");
                fullLaunchString = fullLaunchString.Replace("${assets_index_name}", assetsIndex);
                fullLaunchString = fullLaunchString.Replace("${auth_session}", "00000000000000000000000000000000");
                fullLaunchString = fullLaunchString.Replace("${game_assets}", "resources");
                fullLaunchString = fullLaunchString.Replace("${classpath}", $"{librariesString}{clientJarFile}");
                fullLaunchString = fullLaunchString.Replace("${library_directory}", "libraries");
                fullLaunchString = fullLaunchString.Replace("${classpath_separator}", ";");
                fullLaunchString = fullLaunchString.Replace("${natives_directory}", nativesPath);
                fullLaunchString = fullLaunchString.Replace("\"-Dminecraft.launcher.brand=${launcher_name}\"", "\"-Dminecraft.launcher.brand=vortex-launcher\"");
                fullLaunchString = fullLaunchString.Replace("\"-Dminecraft.launcher.version=${launcher_version}\"", $"\"-Dminecraft.launcher.version=1.0.0\"");

                // Fix any double spaces
                while (fullLaunchString.Contains("  "))
                {
                    fullLaunchString = fullLaunchString.Replace("  ", " ");
                }

                fullLaunchString = fullLaunchString.Replace("\\", "/");

                // Save launch string if enabled
                if (saveLaunchStringCheckBox.IsChecked == true)
                {
                    File.WriteAllText("launch_string.txt", $"\"{javaBinaryPath}\" {fullLaunchString}");
                }

                ProcessStartInfo startInfo = new()
                {
                    FileName = javaBinaryPath,
                    Arguments = fullLaunchString,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                try
                {
                    Process minecraftProcess = Process.Start(startInfo);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error launching Minecraft: {ex.Message}\n\nDetails: {ex.ToString()}",
                        "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Client jar file is missing!", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ParseLibrariesForLaunch(string clientVersion, bool downloadMissing, string existingLibraries = "")
        {
            string versionJsonPath = Path.Combine("versions", clientVersion, $"{clientVersion}.json");
            string libsString = "";

            if (!File.Exists(versionJsonPath))
                return libsString;

            try
            {
                string versionJson = File.ReadAllText(versionJsonPath);

                using JsonDocument doc = JsonDocument.Parse(versionJson);
                JsonElement root = doc.RootElement;
                JsonElement libraries = root.GetProperty("libraries");

                foreach (JsonElement library in libraries.EnumerateArray())
                {
                    bool allowLib = true;

                    // Check rules
                    if (library.TryGetProperty("rules", out JsonElement rules))
                    {
                        foreach (JsonElement rule in rules.EnumerateArray())
                        {
                            string action = rule.GetProperty("action").GetString();

                            if (rule.TryGetProperty("os", out JsonElement os))
                            {
                                string osName = os.GetProperty("name").GetString();

                                if (action == "allow")
                                {
                                    if (osName != "windows")
                                    {
                                        allowLib = false;
                                    }
                                }
                                else // disallow
                                {
                                    if (osName == "windows")
                                    {
                                        allowLib = false;
                                    }
                                }
                            }
                        }
                    }

                    if (!allowLib) continue;

                    string name = library.GetProperty("name").GetString();
                    string[] nameParts = name.Split(':');

                    if (nameParts.Length < 3) continue;

                    // Build library path
                    string domain = nameParts[0].Replace(".", "\\");
                    string artifact = nameParts[1];
                    string version = nameParts[2];
                    string classifier = nameParts.Length > 3 ? nameParts[3] : "";

                    string libBasePath = $"{domain}\\{artifact}";
                    string libPath = $"{libBasePath}\\{version}\\{artifact}-{version}";

                    if (!string.IsNullOrEmpty(classifier))
                    {
                        libPath += $"-{classifier}";
                    }

                    // Skip if already in libraries string
                    bool skipLib = false;
                    if (!string.IsNullOrEmpty(existingLibraries) && existingLibraries.Contains(libBasePath + "\\"))
                    {
                        skipLib = true;
                    }

                    // Extract natives if needed
                    if (library.TryGetProperty("natives", out JsonElement natives))
                    {
                        string nativesPath = Path.Combine("versions", clientVersion, "natives");
                        Directory.CreateDirectory(nativesPath);

                        // The actual extraction will be implemented when needed
                        string nativesJar;

                        if (!libPath.EndsWith("natives-windows"))
                        {
                            nativesJar = Path.Combine("libraries", $"{libPath}-natives-windows.jar");
                        }
                        else
                        {
                            nativesJar = Path.Combine("libraries", $"{libPath}.jar");
                        }

                        if (File.Exists(nativesJar))
                        {
                            Utils.ExtractNatives(nativesJar, nativesPath);
                        }
                    }
                    else if (!skipLib)
                    {
                        libsString += $"{workingDirectory}libraries\\{libPath}.jar;";
                    }
                }
            }
            catch
            {
                // Error handling
            }

            return libsString;
        }

        // WPF Event handlers
        private void PreviewTextInput_Number(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void PreviewTextInput_PlayerName(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new("[^a-zA-Z]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void PreviewKeyDown_NoSpaces(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        private void UseCustomJavaCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            javaPathTextBox.IsEnabled = useCustomJavaCheckBox.IsChecked == true;
            javaComboBox.IsEnabled = useCustomJavaCheckBox.IsChecked != true;
            FindJava();
            SaveSettings();
        }

        private void UseCustomParamsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            launchArgsTextBox.IsEnabled = useCustomParamsCheckBox.IsChecked == true;
            SaveSettings();
        }

        private void AsyncDownloadCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            downloadThreadsTextBox.IsEnabled = asyncDownloadCheckBox.IsChecked == true;
            SaveSettings();
        }

        private void ShowAllVersionsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(versionsManifestString))
            {
                ParseVersionsManifest(showAllVersionsCheckBox.IsChecked == true);
            }
            SaveSettings();
        }

        private void PlayerNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isInitializing) SaveSettings();
        }

        private void RamAmountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isInitializing) SaveSettings();
        }

        private void DownloadThreadsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isInitializing) SaveSettings();
        }

        private void JavaPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isInitializing) SaveSettings();
        }

        private void LaunchArgsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isInitializing) SaveSettings();
        }

        private void VersionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitializing && versionsComboBox.SelectedItem != null)
            {
                SaveSettings();
            }
        }

        private void JavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitializing && javaComboBox.SelectedItem != null)
            {
                SaveSettings();
            }
        }

        private void KeepLauncherOpenCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void DownloadMissingLibsCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void SaveLaunchStringCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }
    }
}