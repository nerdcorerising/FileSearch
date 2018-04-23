using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileSearch
{
    class Program
    {
        static Dictionary<string, FileInfo> s_files = new Dictionary<string, FileInfo>();
        static ReaderWriterLockSlim s_filesLock = new ReaderWriterLockSlim();

        static ConcurrentQueue<string> s_pathsToProcess = new ConcurrentQueue<string>();
        static ManualResetEvent s_pathsEvent = new ManualResetEvent(false);

        static HashSet<string> s_validExtensions = new HashSet<string>(new string[] { ".txt", ".h", ".inl", ".hpp", ".cpp", ".cs" } );

        static void Main(string[] args)
        {
            string path;
            if (args.Length != 1)
            {
                path = "/Users/dave/projects";
            }
            else
            {
                path = args[0];
            }

            FileSystemWatcher watcher = new FileSystemWatcher();
            watcher.Path = path;
            watcher.IncludeSubdirectories = true;
            watcher.Filter = "*";
            watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite 
                                    | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            watcher.EnableRaisingEvents = true;

			watcher.Created += Watcher_Created;
            watcher.Changed += Watcher_Changed;
            watcher.Deleted += Watcher_Deleted;
            watcher.Renamed += Watcher_Renamed;

            Thread t = new Thread(ScanFiles);
            t.Start(path);
            foreach (int i in Enumerable.Range(0, Environment.ProcessorCount))
            {
                Thread temp = new Thread(ScanFilesHelper);
                temp.Start();
            }

            Console.WriteLine("Press q to exit...");
            string line;
            Stopwatch timer = new Stopwatch();
            while ((line = Console.ReadLine()) != "q")
            {
                timer.Restart();
                s_filesLock.EnterReadLock();
                BoyerMoore boyerMoore = new BoyerMoore(line, true);
                Parallel.ForEach(s_files.Keys, key =>
                {
                    //if (s_files[key].Contents.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0)
                    if (boyerMoore.Search(s_files[key].Contents) >= 0)
                    {
                        Console.WriteLine($"File {key} contains string {line}");
                    }
                });

                s_filesLock.ExitReadLock();
                timer.Stop();
                Console.WriteLine($"Search completed in {timer.ElapsedMilliseconds} milliseconds.");
            }
        }

        private static void ScanFiles(object obj)
        {
            string path = obj as string;
            if (path == null)
            {
                throw new ArgumentException();
            }

            Stack<string> paths = new Stack<string>();
            paths.Push(path);
            while (paths.Count > 0)
            {
                string current = paths.Pop();
                foreach (string dir in Directory.EnumerateDirectories(current))
                {
                    paths.Push(dir);
                }

                foreach (string file in Directory.EnumerateFiles(current))
                {
                    string extension = Path.GetExtension(file);
                    if (s_validExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        //PopulateFile(file);
                        s_pathsToProcess.Enqueue(file);
                        s_pathsEvent.Set();
                    }
                }
            }
        }

        private static void ScanFilesHelper()
        {
            while (true)
            {
                s_pathsEvent.WaitOne();

                string path;
                s_pathsToProcess.TryDequeue(out path);

                if (path == null)
                {
                    s_pathsEvent.Reset();
                }
                else
                {
                    PopulateFile(path);
                }
            }
        }

        private static void PopulateFile(string filePath)
        {
            DateTime creation = File.GetCreationTime(filePath);
            filePath = Path.GetFullPath(filePath);

            s_filesLock.EnterReadLock();
            bool contains = s_files.ContainsKey(filePath);
            s_filesLock.ExitReadLock();

            if (!contains)
            {
                FileInfo info;
                if (!s_files.TryGetValue(filePath, out info)
                    || info.LastModified < creation)
                {
                    FileInfo newInfo = new FileInfo();
                    try
                    {
                        //Console.WriteLine($"Reading file {filePath}");
                        using (StreamReader input = new StreamReader(new FileStream(filePath, FileMode.Open)))
                        {
                            newInfo.LastModified = creation;
                            newInfo.Path = filePath;
                            newInfo.Contents = input.ReadToEnd();
                        }

                        s_filesLock.EnterWriteLock();
                        if (!s_files.ContainsKey(filePath)
                             || s_files[filePath].LastModified < creation)
                        {
                            s_files.Add(filePath, newInfo);
                        }
                        s_filesLock.ExitWriteLock();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        //Console.WriteLine($"Can't watch file {filePath}, unauthorized.");
                    }
                }
            }
        }

        private static void Watcher_Created(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File {e.FullPath} created.");
        }

        private static void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File {e.FullPath} changed.");
        }

        private static void Watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            Console.WriteLine($"File {e.FullPath} deleted.");
        }

        private static void Watcher_Renamed(object sender, RenamedEventArgs e)
        {
            Console.WriteLine($"File {e.OldFullPath} renamed to {e.FullPath}.");
        }
    }
}
