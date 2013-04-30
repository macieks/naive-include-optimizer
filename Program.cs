using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace NaiveIncludeOptmizer
{
    class Program
    {
        static string devenvPath;
        static string slnFilename;
        static string backupSlnFilename;
        static string config;
        static string srcPath;
        static string logFilePath = "naive_include_optimizer.out";
        static string progressFile = "naive_include_optimizer.progress";

        static string doneText = "<<DONE>>";

        static void Main(string[] args)
        {
            WriteLine(false, "=== Naive Include Optimizer ===");
            ParseArgs(args);
            VerifyBuild();
            MakeSlnCopy();
            OptimizeSln();
        }

        static void ParseArgs(string[] args)
        {
            if (args.Length != 4)
            {
                PrintUsage();
                Environment.Exit(1);
            }

            devenvPath = args[0];
            slnFilename = args[1];
            backupSlnFilename = slnFilename.Replace(".sln", "_copy.sln");
            config = args[2];
            srcPath = args[3];
        }

        static void MakeSlnCopy()
        {
            try
            {
                File.Copy(slnFilename, backupSlnFilename, true);
            }
            catch (Exception e)
            {
                WriteLine(true, string.Format("ERROR: Failed to make a copy of the solution file {0}, reason: {1}", slnFilename, e.Message));
                Environment.Exit(1);
            }
        }

        static void VerifyBuild()
        {
            WriteLine(false, "Performing initial compilation...");
            try
            {
                if (!Compiles(slnFilename))
                {
                    WriteLine(true, string.Format("ERROR: Solution {0} doesn't compile for specified configuration {1} even without any optimizations.", slnFilename, config));
                    Environment.Exit(2);
                }
            }
            catch (Exception e)
            {
                WriteLine(true, string.Format("ERROR: Failed to perform initial compilation of solution {0} for configuration {1}, reason: {2}", slnFilename, config, e.Message));
                Environment.Exit(2);
            }
        }

        static void OptimizeSln()
        {
            // Get all source files to optimize

            string[] sourceFileNames = null;
            try
            {
                sourceFileNames = Directory.GetFiles(srcPath, "*.cpp", SearchOption.AllDirectories);
                List<string> cppOnly = new List<string>();
                foreach (string sourceFile in sourceFileNames)
                    if (sourceFile.EndsWith(".cpp") && !sourceFile.EndsWith("_backup.cpp"))
                        cppOnly.Add(sourceFile);
                sourceFileNames = cppOnly.ToArray();
                Array.Sort(sourceFileNames);
            }
            catch (Exception e)
            {
                WriteLine(true, string.Format("ERROR: Failed to get source files from directory {0}, reason: {1}", srcPath, e.Message));
                PrintUsage();
                Environment.Exit(3);
            }

            // Optionally resume work from where we stopped

            string startFileName = "";
            try
            {
                startFileName = File.ReadAllText(progressFile);
                if (startFileName == doneText)
                {
                    WriteLine(true, string.Format("Optimization already completed. Delete progress file '{0}' to start from scratch.", progressFile));
                    Environment.Exit(0);
                }

                // Restore original startup file

                try
                {
                    string backupStartFilename = startFileName.Replace(".cpp", "_backup.cpp");
                    File.Copy(backupStartFilename, startFileName);
                }
                catch
                {
                }

                if (startFileName.Length > 0)
                    WriteLine(false, string.Format("Resuming from {0} file", startFileName));
            }
            catch
            {
            }

            // Optimize all source files

            foreach (string sourceFileName in sourceFileNames)
                if (startFileName == "" || startFileName.CompareTo(sourceFileName) <= 0)
                    OptimizeSourceFile(sourceFileName);

            File.WriteAllText(progressFile, doneText);

            // Delete backup sln file

            try
            {
                File.Delete(backupSlnFilename);
            }
            catch
            {
            }

            WriteLine(true, "DONE!");
        }

        static void PrintUsage()
        {
            WriteLine(true, "Naive Include Optimizer by Maciej Sawitus ver 1.0");
            WriteLine(true, "Usage:");
            WriteLine(true, "  NaiveIncludeOptimizer.exe <devenv-path> <sln-path> <config> <src-dir>");
            WriteLine(true, "Info:");
            WriteLine(true, "  <devenv-path> - path to your Visual Studio devend compiler, e.g.: C:\\Program Files (x86)\\Microsoft Visual Studio 11.0\\Common7\\IDE\\devenv.exe");
            WriteLine(true, "  <sln-path>    - path to your solution, e.g. C:\\my_project\\my_project.sln");
            WriteLine(true, "  <config>      - solution configuration to be used, e.g. Debug");
            WriteLine(true, "  <src-dir>     - path to directory containing all source files to be optimized, e.g. C:\\my_project\\src");
            WriteLine(true, "Example command line:");
            WriteLine(true, "  NaiveIncludeOptimizer.exe \"C:\\Program Files (x86)\\Microsoft Visual Studio 11.0\\Common7\\IDE\\devenv.exe\" \"C:\\my_project\\my_project.sln\" \"Debug\" \"C:\\my_project\\src\"");
        }

        static void OptimizeSourceFile(string fileName)
        {
            WriteLine(false, string.Format("Optimizing {0}...", fileName));

            string backupFilename = fileName.Replace(".cpp", "_backup.cpp");
            try
            {
                // Update progress file

                File.WriteAllText(progressFile, fileName);

                // Make source file backup

                File.Copy(fileName, backupFilename, true);

                // Try to get rid of all #includes - one by one

                string[] lines = File.ReadAllLines(fileName);
                bool[] removedLines = new bool[lines.Length];
                for (int i = 0; i < lines.Length; i++)
                {
                    removedLines[i] = false;

                    string line = lines[i];
                    if (!line.Contains("#include") || line.StartsWith("//"))
                        continue;

                    lines[i] = "";

                    File.WriteAllLines(fileName, lines); // Overwrite source file with "optimized" version
                    if (!Compiles(backupSlnFilename))
                        lines[i] = line;
                    else
                    {
                        removedLines[i] = true;
                        ReportRedundantInclude(line);
                    }
                }

                // Clean up source file after optimization (get rid of empty lines where #includes were removed)

                List<string> finalLines = new List<string>();
                for (int i = 0; i < lines.Length; i++)
                    if (!removedLines[i])
                        finalLines.Add(lines[i]);

                File.WriteAllLines(fileName, finalLines.ToArray());

                // Update progress file

                File.WriteAllText(progressFile, "");

                // Get rid of the backup file

                File.Delete(backupFilename);
            }
            catch (Exception e)
            {
                WriteLine(true, string.Format("ERROR: Failed to optimize file: {0}", e.Message));
                try
                {
                    // If failed recover original source file

                    File.Copy(backupFilename, fileName, true);
                    File.Delete(backupFilename);
                }
                catch (Exception e2)
                {
                    WriteLine(true, string.Format("ERROR: Failed to revert original copy of the {0} file, reason: {1}", fileName, e2.Message));
                }
            }
        }

        static bool Compiles(string slnFileName)
        {
            bool readOutput = false;

            try
            {
                // Try to compile solution

                Process process = new Process();
                if (readOutput)
                {
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardOutput = true;
                }
                process.StartInfo.FileName = devenvPath;
                process.StartInfo.Arguments = "\"" + slnFileName + "\" /Build \"" + config + "\"";
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();

                if (readOutput)
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        static void ReportRedundantInclude(string line)
        {
            WriteLine(false, string.Format("  Redundant {0}", line));
        }

        static void WriteLine(bool error, string line)
        {
            (error ? Console.Error : Console.Out).WriteLine(line);
            File.AppendAllText(logFilePath, line + "\n");
        }
    }
}
