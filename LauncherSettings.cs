
namespace VortexNet
{
    public class LauncherSettings
    {
        // User settings
        public string Language { get; set; } = "EN";
        public string PlayerName { get; set; } = "Player";
        public string RamAmount { get; set; } = "2500";
        public string ChosenVersion { get; set; } = "";
        public bool UseCustomJava { get; set; } = false;
        public string CustomJavaPath { get; set; } = @"C:\jre8\bin\javaw.exe";
        public int DownloadThreads { get; set; } = 20;
        public bool AsyncDownload { get; set; } = true;
        public bool KeepLauncherOpen { get; set; } = true;
        public bool DownloadMissingLibs { get; set; } = true;
        public bool SaveLaunchString { get; set; } = false;
        public bool UseCustomParameters { get; set; } = false;
        public string LaunchArguments { get; set; } = "-Xss1M -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M";
        public bool ShowAllVersions { get; set; } = false;
        public int LastProfilesJsonSize { get; set; } = 0;
    }
}
