﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mamesaver.Models.Configuration;
using Mamesaver.Services.Windows;
using Serilog;

namespace Mamesaver.Services.Mame
{
    /// <summary>
    ///     Invokes the MAME executable configured in <see cref="Settings"/>.
    /// </summary>
    public class MameInvoker : IDisposable
    {
        private readonly Settings _settings;
        private readonly List<Process> _processes = new List<Process>();

        public MameInvoker(Settings settings) => _settings = settings;

        /// <summary>
        ///     Kills a MAME process.
        /// </summary>
        public void Stop(Process process)
        {
            if (process == null || process.HasExited) return;

            Log.Debug("Stopping MAME; pid: {pid}", process.Id);

            try
            {
                // Minimise and then exit. Minimising it makes it disappear instantly.
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    WindowsInterop.MinimizeWindow(process.MainWindowHandle);
                    process.CloseMainWindow();

                    Log.Debug("Waiting for MAME to exit");
                    if (!process.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds))
                    {
                        Log.Warning("Timeout waiting for MAME to exit; killing MAME");
                        process.Kill();
                    }
                }
                else
                {
                    Log.Debug("Killing MAME as no window handle");
                    process.Kill();
                }

                process.WaitForExit();
                Log.Debug("MAME stopped; pid {pid}", process.Id);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error stopping MAME");
            }
        }

        /// <summary>
        ///     Invokes and starts MAME, returning the created process.
        /// </summary>
        /// <param name="arguments">arguments to pass to Mame</param>

        public Process Run(params string[] arguments) => Run(true, arguments);

        /// <summary>
        ///     Invokes MAME, returning the created process.
        /// </summary>
        /// <param name="start">whether to start the MAME process</param>
        /// <param name="arguments">arguments to pass to Mame</param>
        public Process Run(bool start, params string[] arguments)
        {
            Log.Information("Invoking MAME with arguments: {arguments}", string.Join(" ", arguments));

            var execPath = _settings.ExecutablePath;
            if (!File.Exists(execPath)) throw new FileNotFoundException("MAME path not found", execPath);

            var psi = new ProcessStartInfo(execPath)
            {
                Arguments = string.Join(" ", arguments),
                WorkingDirectory = Directory.GetParent(execPath).ToString(),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                var process = new Process { StartInfo = psi };
                if (start)
                {
                    if (!process.Start()) throw new InvalidOperationException($"MAME process not started: {psi.FileName} {psi.Arguments}");
                    Log.Debug("MAME started; pid: {pid}", process.Id);
                }

                process.EnableRaisingEvents = true;

                // Register process so we can terminate all processes when the container is disposed
                Register(process);

                return process;
            }
            catch (Exception e)
            {
                Log.Error(e, "Unable to run MAME: {filename} {arguments}", psi.FileName, psi.Arguments);
                throw;
            }
        }

        /// <summary>
        ///     Invokes MAME, returning the standard output stream.
        /// </summary>
        /// <param name="arguments">arguments to pass to MAME</param>
        public StreamReader GetOutput(params string[] arguments) => Run(arguments).StandardOutput;


        /// <summary>
        ///     Registers a MAME process for automatic termination on shutdown.
        /// </summary>
        /// <param name="process"></param>
        public void Register(Process process) => _processes.Add(process);

        public virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            TryStopProcesses();
        }

        /// <summary>
        ///     Tries to stop all registered MAME processes.
        /// </summary>
        private void TryStopProcesses()
        {
            _processes.ForEach(process =>
            {
                try
                {
                    // Stop MAME and wait for it to terminate
                    Stop(process);
                }
                catch (InvalidOperationException)
                {
                    Log.Warning("Unable to stop MAME; it may not have fully started.");
                }
            });
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~MameInvoker() => Dispose(false);
    }
}