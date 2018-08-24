using System;
using System.Xml.Serialization;

namespace Mamesaver.Models.Settings
{
    [Serializable, XmlRoot("GeneralSettings")]
    public class GeneralSettings
    {
        /// <summary>
        ///     The path to the MAME executable file - including the filename and extension. eg: C:\MAME\MAME32.EXE
        /// </summary>
        [XmlElement("executablePath")]
        public string ExecutablePath { get; set; }

        /// <summary>
        ///     The time to run each game
        /// </summary>
        [XmlElement("minutesPerGame")]
        public int MinutesPerGame { get; set; }

        /// <summary>
        ///     The options to send to the command line when MAME runs the game.
        /// </summary>
        [XmlElement("commandLineOptions")]
        public string CommandLineOptions { get; set; }

        /// <summary>
        ///     Whether the game should be displayed on all screens.
        /// </summary>
        [XmlElement("cloneScreen")]
        public bool CloneScreen { get; set; }

        [XmlElement("layoutSettings")]
        public LayoutSettings LayoutSettings { get; set; }

        public GeneralSettings()
        {
            ExecutablePath = @"C:\MAME\MAME64.EXE";
            CommandLineOptions = "-skip_gameinfo -nowindow -noswitchres -sleep -triplebuffer -sound none";
            MinutesPerGame = 5;
            CloneScreen = true;
            LayoutSettings = new LayoutSettings();
        }
    }
}