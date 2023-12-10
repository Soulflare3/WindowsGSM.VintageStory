using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;
using WindowsGSM.GameServer.Engine;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace WindowsGSM.Plugins
{
    public class VintageStory
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.VintageStory", // WindowsGSM.XXXX
            author = "Soul",
            description = "A plugin version of the Vintage Story Dedicated server for WindowsGSM",
            version = "1.0",
            url = "https://github.com/Soulflare3/WindowsGSM.VintageStory", // Github repository link (Best practice)
            color = "#ffffff" // Color Hex
        };

        // - Standard Constructor and properties
        public VintageStory(ServerConfig serverData) => _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;

        // - Game server Fixed variables
        public string FullName = "Vintage Story Dedicated Plugin";
        public string StartPath = "VintageStoryServer.exe";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public dynamic QueryMethod = null;

        public string Port = "42420";
        public string QueryPort = "42420";
        public string Defaultmap = "default";
        public string Maxplayers = "16";
        public string Additional = "--dataPath ./data";

        // - Create a default cfg for the game server after installation
        public async void CreateServerCFG()
        {
            //Download serverconfig.json
            var replaceValues = new List<(string, string)>()
            {
                ("{{ServerName}}", _serverData.ServerName),
                ("{{Port}}", _serverData.ServerPort),
                ("{{MaxClients}}", _serverData.ServerMaxPlayer)
            };

            await Github.DownloadGameServerConfig(ServerPath.GetServersServerFiles(_serverData.ServerID, "data", "serverconfig.json"), "Vintage Story Dedicated Server", replaceValues);
        }

        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            string exePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(exePath))
            {
                Error = $"{Path.GetFileName(exePath)} not found ({exePath})";
                return null;
            }

            Process p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = Directory.GetParent(exePath).FullName,
                    FileName = exePath,
                    Arguments = _serverData.ServerParam,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            if (AllowsEmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return p;
            }

            p.Start();
            return p;
        }

        // - Stop server function
        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                if (p.StartInfo.RedirectStandardInput)
                {
                    p.StandardInput.WriteLine("/stop");
                }
                else
                {
                    ServerConsole.SendMessageToMainWindow(p.MainWindowHandle, "/stop");
                }
            });
        }

        // - Install server function
        public async Task<Process> Install()
        {
            string version = await GetRemoteBuild();
            if (version == null) { return null; }
            string zipName = $"vs_server_win-x64_{version}.zip";
            string address = $"https://cdn.vintagestory.at/gamefiles/stable/{zipName}";
            string zipPath = ServerPath.GetServersServerFiles(_serverData.ServerID, zipName);

            // Download vs_server_win-x64_{version}.zip from https://cdn.vintagestory.at/gamefiles/stable/
            using (WebClient webClient = new WebClient())
            {
                try { await webClient.DownloadFileTaskAsync(address, zipPath); }
                catch
                {
                    Error = $"Fail to download {zipName}";
                    return null;
                }
            }

            // Extract vs_server_win-x64_{version}.zip
            if (!await FileManagement.ExtractZip(zipPath, Directory.GetParent(zipPath).FullName))
            {
                Error = $"Fail to extract {zipName}";
                return null;
            }

            // Delete vs_server_win-x64_{version}.zip, leave it if fail to delete
            await FileManagement.DeleteAsync(zipPath);

            return null;
        }

        // - Update server function
        public async Task<Process> Update()
        {
            // Backup the data folder
            string dataPath = ServerPath.GetServersServerFiles(_serverData.ServerID, "data");
            string tempPath = ServerPath.GetServers(_serverData.ServerID, "__temp");
            bool needBackup = Directory.Exists(dataPath);
            if (needBackup)
            {
                if (Directory.Exists(tempPath))
                {
                    if (!await DirectoryManagement.DeleteAsync(tempPath, true))
                    {
                        Error = "Fail to delete the temp folder";
                        return null;
                    }
                }

                if (!await Task.Run(() =>
                {
                    try
                    {
                        CopyDirectory(dataPath, tempPath, true);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Error = e.Message;
                        return false;
                    }
                }))
                {
                    return null;
                }
            }

            // Delete the serverfiles folder
            if (!await DirectoryManagement.DeleteAsync(ServerPath.GetServersServerFiles(_serverData.ServerID), true))
            {
                Error = "Fail to delete the serverfiles";
                return null;
            }

            // Recreate the serverfiles folder
            Directory.CreateDirectory(ServerPath.GetServersServerFiles(_serverData.ServerID));

            if (needBackup)
            {
                // Restore the data folder
                if (!await Task.Run(() =>
                {
                    try
                    {
                        CopyDirectory(tempPath, dataPath, true);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Error = e.Message;
                        return false;
                    }
                }))
                {
                    return null;
                }

                await DirectoryManagement.DeleteAsync(tempPath, true);
            }

            // Update the server by install again
            await Install();

            // Return is valid
            if (IsInstallValid())
            {
                return null;
            }

            Error = "Update fail";
            return null;
        }

        // - Check if the installation is successful
        public bool IsInstallValid()
        {
            string exePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            return File.Exists(exePath);
        }

        public bool IsImportValid(string path)
        {
            string exePath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(exePath);
        }

        // - Get Local server version
        public string GetLocalBuild()
        {
            // Get local version in VintageStoryServer.exe
            string exePath = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(exePath))
            {
                Error = $"{StartPath} is missing.";
                return string.Empty;
            }

            return FileVersionInfo.GetVersionInfo(exePath).ProductVersion; // return "1.12.14"
        }

        // - Get Latest server version
        public async Task<string> GetRemoteBuild()
        {
            // Get latest build in https://aur.archlinux.org/cgit/aur.git/log/?h=vintagestory with regex
            try
            {
                using (WebClient webClient = new WebClient())
                {
                    string html = await webClient.DownloadStringTaskAsync("https://aur.archlinux.org/cgit/aur.git/log/?h=vintagestory");
                    Regex regex = new Regex(@"(\d{1,}\.\d{1,}\.\d{1,})<\/a>"); // Match "1.12.14</a>"
                    return regex.Match(html).Groups[1].Value; // Get first group -> "1.12.14"
                }
            }
            catch
            {
                Error = "Fail to get remote build";
                return string.Empty;
            }
        }

        //Source: https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
        //Adding to replace reliance on VisualBasic library
        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
    }
}