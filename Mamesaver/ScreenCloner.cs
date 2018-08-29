﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Serilog;

namespace Mamesaver
{
    /// <summary>
    ///     Clones a MAME screen onto a number of blank screens.
    /// </summary>
    internal class ScreenCloner : IDisposable
    {
        private readonly MameScreen _sourceScreen;
        private readonly CaptureScreen _captureScreen;
        private List<BlankScreen> _blankScreens;
        private Timer _refreshTimer;

        public ScreenCloner(CaptureScreen captureScreen, MameScreen mameScreen)
        {
            _captureScreen = captureScreen;
            _sourceScreen = mameScreen;
        }

        public void Clone(List<BlankScreen> blankScreens)
        {
            _blankScreens = blankScreens;

            // is there anything to clone?
            if (_sourceScreen == null || !_blankScreens.Any())
            {
                Log.Information("No source or destinations screens to clone");
                return;
            }

            _blankScreens.ForEach(screen => Log.Information("Destination screen found {device} {bounds}", screen.Screen.DeviceName, screen.Screen.Bounds));

            _refreshTimer = new Timer
            {
                Enabled = true,
                Interval = 1000 / 30 // fps - TODO verify CPU usage
            };
            _refreshTimer.Tick += _refreshTimer_Tick;
        }

        public void Dispose()
        {
            Log.Debug("{class} Dispose()", GetType().Name);

            _sourceScreen?.Dispose();
            _refreshTimer?.Dispose();
            _captureScreen?.Dispose();
        }

        public void Stop()
        {
            _refreshTimer?.Stop();
        }

        private void _refreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _blankScreens.ForEach(screen =>
                {
                    Log.Verbose($"Cloning to screen {screen.Screen.DeviceName}");
                    _captureScreen.CloneTo(screen.HandleDeviceContext, screen.Screen.Bounds);
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error cloning screens on timer");
            }
        }
    }
}
