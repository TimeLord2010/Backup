using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Runtime.InteropServices;
using static System.Console;

namespace Backup {

    class Program {

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        static void Main(string[] args) {
            Title = "Backup";
            int width = 60;
            CursorVisible = false;
            SetWindowSize(width, 30);
            SetBufferSize(width, 30);
            if (!File.Exists("ConsoleMetaData.txt")) {
                //File.Create("ConsoleMetaData.txt");
                IO.WriteTextFile("ConsoleMetaData.txt", "visible:false", Encoding.UTF8);
            }
            var handle = GetConsoleWindow();
            // Show
            while (true) {
                ShowWindow(handle, SW_SHOW);
                Validate();
                var dt = Save().AddDays(7);
                Clear();
                ShowWindow(handle, SW_HIDE);
                bool shown = false;
                while (true) {
                    var should_show = ShouldShow;
                    if (shown != should_show) {
                        ShowWindow(handle, should_show? SW_SHOW : SW_HIDE);
                        shown = should_show;
                    }
                    var remaining = dt - DateTime.Now;
                    if (dt < DateTime.Now) break;
                    WriteLine($"Remaining: {remaining.Days} {remaining:hh\\:mm\\:ss}");
                    CursorTop = 0;
                    CursorLeft = 0;
                    Thread.Sleep(5000);
                }
            }
        }

        static List<FileSystemInfo> ToBackupFilesOrDirectory {
            get {
                var list = new List<FileSystemInfo>();
                using (var sr = new StreamReader("Backups.txt", Encoding.UTF8)) {
                    while (!sr.EndOfStream) {
                        var line = sr.ReadLine();
                        line = line.Replace("{CurrentUser}", Environment.UserName);
                        if (Directory.Exists(line)) {
                            list.Add(new DirectoryInfo(line));
                        } else if (File.Exists(line)) {
                            list.Add(new FileInfo(line));
                        }
                    }
                }
                return list;
            }
        }

        static List<DirectoryInfo> OutputFolders {
            get {
                var list = new List<DirectoryInfo>();
                using (var sr = new StreamReader("Outputs.txt", Encoding.UTF8)) {
                    while (!sr.EndOfStream) {
                        var line = sr.ReadLine();
                        var matches = Regex.Matches(line, "<.*>");
                        if (matches.Count > 1) {
                            WriteLine($"Directory ({line}) cannot have more than one drive letter.");
                            continue;
                        } else if (matches.Count == 1) {
                            var match = matches[0];
                            var value = match.Value;
                            value = value.Substring(1, value.Length - 2);
                            var di = DriveInfo.GetDrives()
                                .Where(x => FuncUtils.AnyEquals(x.DriveType, DriveType.Removable, DriveType.Fixed))
                                .FirstOrDefault(x => x.VolumeLabel == value);
                            if (di == default) {
                                WriteLine($"Drive ({value}) not found.");
                                continue;
                            }
                            line = line.Replace($"<{value}>", di.Name);
                        }
                        if (!Directory.Exists(line)) {
                            WriteLine($"Creating directory: {line}.");
                            Directory.CreateDirectory(line);
                        }
                        list.Add(new DirectoryInfo(line));
                    }
                }
                return list;
            }
        }

        static bool ShouldShow {
            get {
                bool result = false;
                IO.ReadTextFile("ConsoleMetaData.txt", (line) => {
                    line = line.ToLower();
                    if (Regex.IsMatch(line, @"^visible:\s*(true|false)\s*$")) {
                        for (var match = Regex.Match(line, @"^visible:\s*(true|false)\s*$"); match.Success; match = match.NextMatch()) {
                            result = match.Groups[1].Value == "true";
                            return false;
                        }
                    }
                    return true;
                });
                return result;
            }
        }

        static void Validate() {
            string fn = null;
            if (!File.Exists($"Outputs.txt")) {
                fn = "Outputs.txt";
                ForegroundColor = ConsoleColor.Red;
                WriteLine($"A file named '{fn}' is required in the path '{Environment.CurrentDirectory}' for the program to run.");
                ForegroundColor = ConsoleColor.Gray;
                WriteLine($"The file should contain folders separated by new lines. These folders are the options for output for the backups this program will make.");
                WriteLine($"Remarks:");
                WriteLine($"If the drive in an output option has not enough avaliable space and the program has made at least two backups in that folder, then it will delete the oldest and try again.");
                WriteLine("Otherwise, the program will try the other backup option.");
            }
            if (!File.Exists("Backups.txt")) {
                fn = "Backups.txt";
                ForegroundColor = ConsoleColor.Red;
                WriteLine($"A file named '{fn}' is required in the path '{Environment.CurrentDirectory}' for the program to run.");
                ForegroundColor = ConsoleColor.Gray;
                WriteLine($"The file should contain folders or files separated by new lines refering to the objects to backup.");
            }
            if (fn == null) {
                return;
            }
            WriteLine($"Press any key to close...");
            ReadKey();
            Environment.Exit(Environment.ExitCode);
        }

        static DateTime Save() {
            var to_backup = ToBackupFilesOrDirectory;
            long necessary_size = IO.TotalSize(to_backup);
            foreach (var output_folder in OutputFolders) {
                var drive_letter = Path.GetPathRoot(output_folder.FullName);
            A:
                var drive_info = new DriveInfo(drive_letter);
                if (drive_info.AvailableFreeSpace < necessary_size) {
                    var dirs = output_folder.GetDirectories("*] Backup");
                    if (dirs.Length > 1) {
                        Directory.Delete(dirs[0].FullName, true);
                        goto A;
                    }
                }
                var path = $"{output_folder.FullName}\\[{DateTime.Now:yyyy-MM-dd}] Backup";
                if (Directory.Exists(path)) {
                    WriteLine($"Backup already made.");
                    Thread.Sleep(3000);
                    var dt = DateTime.Now;
                    return (new DateTime(dt.Year, dt.Month, dt.Day)).AddMinutes(1);
                }
                DirectoryInfo base_folder = null;
                var lastsbackups = output_folder.GetDirectories("*] Backup");
                if (lastsbackups.Length > 0) {
                    base_folder = lastsbackups.Last();
                }
                var out_folder = new DirectoryInfo(path);
                foreach (var fsi in to_backup) {
                    try {
                        if (fsi is FileInfo fi) {
                            if (!ShouldCopy(base_folder, fi)) {
                                continue;
                            }
                            WriteLine($"Copying file: {fi.FullName}");
                            File.Copy(fi.FullName, $"{out_folder.FullName}\\{fi.Name}");
                        } else if (fsi is DirectoryInfo di) {
                            var last = new DirectoryInfo(base_folder.FullName + $"\\{di.Name}");
                            WriteLine($"Copying directory: {di.FullName}");
                            IO.CopyDirectory(di, new DirectoryInfo($"{out_folder.FullName}\\{di.Name}"), (x) => {
                                if (x is FileInfo fi2) {
                                    if (!ShouldCopy(last, fi2)) {
                                        return false;
                                    }
                                    WriteLine($"Copying file: {x.FullName}");
                                } else if (x is DirectoryInfo di2) {
                                    if (di2.Parent.Name == last.Name) {
                                        last = new DirectoryInfo(last.FullName + $"\\{di2.Name}");
                                    } else {
                                        while (di2.Parent.Name != last.Name || di2.Parent.Parent.Name != last.Parent.Name) {
                                            if (last.Parent == null || last.FullName == base_folder.FullName + $"\\{di.Name}") {
                                                break;
                                            }
                                            last = last.Parent;
                                        }
                                        last = new DirectoryInfo(last.FullName + $"\\{di2.Name}");
                                    }
                                    //WriteLine($"Copying directory: {x.FullName}");
                                }
                                return true;
                            }, (ex, fi2) => {
                                ForegroundColor = ConsoleColor.Red;
                                WriteLine($"Error while copy the file ({fi2.FullName}): {ex.Message}");
                                ForegroundColor = ConsoleColor.Gray;
                                return true;
                            });
                        }
                    } catch (Exception ex) {
                        ForegroundColor = ConsoleColor.Red;
                        WriteLine($"{ex.GetType()}: {ex.Message}");
                        ForegroundColor = ConsoleColor.Gray;
                    }
                }
            }
            return DateTime.Now;
        }

        static bool ShouldCopy(DirectoryInfo last, FileInfo fi) {
            if (last != null && File.Exists(last.FullName + $"\\{fi.Name}")) {
                var last_fi = new FileInfo(last.FullName + $"\\{fi.Name}");
                if (File.GetLastWriteTime(fi.FullName) <= File.GetLastWriteTime(last_fi.FullName)) {
                    return false;
                }
            }
            return true;
        }

    }
}
