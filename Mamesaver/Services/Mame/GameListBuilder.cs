﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Mamesaver.Models;
using Mamesaver.Models.Configuration;
using Mamesaver.Models.Extensions;
using Mamesaver.Services.Categories;
using Serilog;

namespace Mamesaver.Services.Mame
{
    public class GameListBuilder
    {
        private readonly AdvancedSettings _advancedSettings;
        private readonly MameInvoker _invoker;
        private readonly MamePathManager _pathManager;
        private readonly CategoryParser _categoryParser;

        /// <summary>
        ///     Number of ROMs to process per batch. Rom files are batched when passing as arguments to Mame both 
        ///     to minimise processing time and to allow a visual indicator that games are being processed.
        /// </summary>
        private const int DefaultRomsPerBatch = 50;

        /// <summary>
        ///     Settings for parsing MAME's listxml output
        /// </summary>
        private readonly XmlReaderSettings _readerSettings;
       
        public GameListBuilder(
            AdvancedSettings advancedSettings, 
            MameInvoker invoker, 
            MamePathManager pathManager, 
            CategoryParser categoryParser)
        {
            _advancedSettings = advancedSettings;
            _invoker = invoker;
            _pathManager = pathManager;
            _categoryParser = categoryParser;

            _readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse };
        }

        /// <summary>
        ///     Returns a <see cref="List{T}" /> of <see cref="SelectableGame" />s which are read from
        ///     the full list and then merged with the verified ROMs list. The games which are returned
        ///     all have a "good" status on their drivers. This check also eliminates BIOS ROMS.
        /// </summary>
        /// <param name="progressCallback">Callback invoked with percentage complete</param>
        /// <returns>Returns a <see cref="List{T}" /> of <see cref="SelectableGame" />s</returns>
        public List<SelectableGame> GetGameList(Action<int> progressCallback)
        {
            var games = new List<SelectableGame>();

            // Enrich game metadata for each verified game
            var verifiedGames = GetVerifiedSets(progressCallback).Keys.ToList();
            var romFiles = GetRomFiles();

            try
            {
                return GetGameList(progressCallback, romFiles, verifiedGames, games, DefaultRomsPerBatch);
            }
            catch (XmlException)
            {
                // Fallback for old versions of MAME which don't support extracting game details for multiple games at a time
                return GetGameList(progressCallback, romFiles, verifiedGames, games, 1);
            }
        }

        private List<SelectableGame> GetGameList(Action<int> progressCallback, List<string> romFiles, List<string> verifiedGames, List<SelectableGame> games, int romsPerBatch)
        {
            // Get details for each verified ROM
            var index = 0;

            // The loop to add each game detail is based on the number of verified games, hence
            // we need to register the unverified games for our total process count.
            var romsProcessed = romFiles.Count + (romFiles.Count - verifiedGames.Count);

            List<string> romsToProcess;
            while ((romsToProcess = verifiedGames.Skip(index).Take(romsPerBatch).ToList()).Any())
            {
                using (var stream = GetGameDetails(romsToProcess))
                {
                    games.AddRange(GetRomDetails(stream));
                }

                index += romsPerBatch;
                romsProcessed += romsToProcess.Count;

                // There are two distinct phases in the game list builder, so we are halving the processed count
                Callback(progressCallback, romsProcessed / 2f, romFiles.Count);
            }

            return games;
        }

        /// <summary>
        ///     Gets the ROM details for a single game.
        /// </summary>
        public SelectableGame GetRomDetails(string game)
        {
            using (var stream = GetGameDetails(new List<string> { game }))
            {
                return GetRomDetails(stream, false).First();
            }
        }

        /// <summary>
        ///     Extracts ROM metadata for display from a XML stream from MAME.
        /// </summary>
        private List<SelectableGame> GetRomDetails(StreamReader stream, bool verify = true)
        {
            var games = new List<SelectableGame>();
            var validGameStatuses = ValidGameStatuses();

            using (var reader = XmlReader.Create(stream, _readerSettings))
            {
                reader.ReadStartElement("mame");

                // Read each machine, enriching metadata for verified sets
                while (reader.Read() && (reader.Name == "machine" || reader.Name == "game"))
                {
                    // Read game metadata
                    var element = (XElement)XNode.ReadFrom(reader);

                    var name = element.Attribute("name")?.Value;
                    if (name == null) continue;

                    // Perform initial validity checks
                    if (!IsGameValid(element, name)) continue;

                    // Retrieve and verify driver and emulation status
                    var driver = element.Element("driver");
                    if (driver == null)
                    {
                        Log.Information("{name} not added to game list as it has no driver specified", name);
                        continue;
                    }

                    // Skip games which aren't sufficiently emulated
                    var status = driver.Attribute("status")?.Value;
                    if (verify && !validGameStatuses.Contains(status))
                    {
                        Log.Information("{name} not added to game list because it has a status of {status}", name, status);
                        continue;
                    }

                    var year = element.Element("year")?.Value ?? "";
                    var manufacturer = element.Element("manufacturer")?.Value ?? "";
                    var description = element.Element("description")?.Value ?? "";
                    var rotation = element.Element("display")?.Attribute("rotate")?.Value ?? "";

                    // Read category
                    var categoryDetail = _categoryParser.GetCategory(name);

                    games.Add(new SelectableGame
                    {
                        Name = name,
                        Description = description,
                        Year = year,
                        Manufacturer = manufacturer,
                        Rotation = rotation,
                        Category = categoryDetail.Category,
                        Subcategory = categoryDetail.Subcategory,
                        Selected = false
                    });
                }
            }

            return games;
        }

        /// <summary>
        ///     Performs preliminary validation checks against a game, verifying that it's runnable, isn't a BIOS
        ///     or mechanical.
        /// </summary>
        /// <param name="element">Machine element from MAME's <c>-listxml</c> output</param>
        /// <param name="name">name of game being evaluated</param>
        private static bool IsGameValid(XElement element, string name)
        {
            if (element.ToBoolean("isdevice") == true) return false;

            // Verify that the ROM isn't a BIOS
            if (element.ToBoolean("isbios") == true)
            {
                Log.Information("{name} not added to game list as it is a BIOS", name);
                return false;
            }

            // Verify that the game isn't mechanical
            if (element.ToBoolean("ismechanical") == true)
            {
                Log.Information("{name} not added to game list as it is mechanical", name);
                return false;
            }

            // Verify that the game is runnable
            if (element.ToBoolean("runnable") == false)
            {
                Log.Information("{name} not added to game list as it is not runnable", name);
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Returns a <see cref="IDictionary{TKey,TValue}" /> filled with the names of games which are
        ///     verified to work. Only the ones marked as good are returned. The clone names
        ///     are returned in the value of the dictionary while the name is used as the key.
        /// </summary>
        private IDictionary<string, string> GetVerifiedSets(Action<int> progressCallback)
        {
            var verifiedSets = GetVerifiedSets(progressCallback, DefaultRomsPerBatch);
            if (verifiedSets.Any()) return verifiedSets;

            // Cater for old versions of MAME which only support verifying a single ROM at a time
            verifiedSets = GetVerifiedSets(progressCallback, 1);
            return verifiedSets;
        }

        private IDictionary<string, string> GetVerifiedSets(Action<int> progressCallback, int romsPerBatch)
        {
            var verifiedRoms = new Dictionary<string, string>();
            var regex = new Regex(@"romset (\w*)(?:\s\[(\w*)\])? is good"); //only accept the "good" ROMS

            var romFiles = GetRomFiles();

            // Verify each ROM in directory
            var index = 0;
            var filesProcessed = 0;

            List<string> filesToVerify;
            while ((filesToVerify = romFiles.Skip(index).Take(romsPerBatch).ToList()).Any())
            {
                var arguments = new List<string> { "-verifyroms" }.Concat(filesToVerify).ToArray();
                using (var stream = _invoker.GetOutput(arguments))
                {
                    var output = stream.ReadToEnd();

                    var matches = regex.Matches(output);
                    for (var i = 0; i < matches.Count; i++)
                    {
                        var match = matches[i];
                        verifiedRoms[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                }

                index += romsPerBatch;
                filesProcessed += filesToVerify.Count;

                // There are two distinct phases in the game list builder, so we are halving the processed count
                Callback(progressCallback, filesProcessed / 2f, romFiles.Count);
            }

            return verifiedRoms;
        }

        /// <summary>
        ///     Invokes the callback with the percentage of game list construction completed.
        /// </summary>
        /// <param name="callback">callback to invoke</param>
        /// <param name="processed">number of ROM files processed</param>
        /// <param name="romCount">total number of ROM files</param>
        private void Callback(Action<int> callback, float processed, int romCount)
        {
            var percentage = (int) Math.Round(processed / romCount  * 100);
            callback(percentage);
        }

        /// <summary>
        ///     Returns a list of the base name of ROMs in the MAME ROM directories.
        /// </summary>
        /// <remarks>
        ///     It is assumed that ROMs are zipped.
        /// </remarks>
        public List<string> GetRomFiles()
        {
            var roms = new List<string>();

            foreach (var path in GetRomPaths())
            {
                var romFiles = Directory.GetFiles(path, "*.zip");
                roms.AddRange(romFiles.Select(Path.GetFileNameWithoutExtension));
            }

            return roms;
        }

        /// <summary>
        ///     Retrieves absolute paths to the ROM directories as configured by MAME.
        /// </summary>
        /// <remarks>
        ///     Multiple ROM directories can be specified in Mame by separating directories by semicolons.
        /// </remarks>
        private List<string> GetRomPaths() => GetConfigPaths("rompath");

        /// <summary>
        ///     Retrieves absolute paths to the art path directories as configured by MAME.
        /// </summary>
        /// <remarks>
        ///     Multiple art directories can be specified in MAME by separating directories by semicolons.
        /// </remarks>
        public List<string> GetArtPaths() => GetConfigPaths("artpath");

        /// <summary>
        ///     Returns the value of a path element in the <c>mame.ini</c> file.
        /// </summary>
        /// <param name="key">key name</param>
        /// <returns>list of absolute paths</returns>
        public List<string>GetConfigPaths(string key)
        {
            Log.Debug("Getting MAME {key}", key);

            using (var stream = _invoker.GetOutput("-showconfig"))
            {
                string line;
                while ((line = stream.ReadLine()) != null)
                {
                    var absolutePaths = _pathManager.ExtractConfigPaths(key, line);
                    if (absolutePaths == null) continue;

                    return absolutePaths;
                }
            }

            throw new InvalidOperationException("Unable to retrieve ROM paths");
        }

        /// <summary>
        ///     Emulation status of games which are added to the game list.
        /// </summary>
        private List<string> ValidGameStatuses()
        {
            var statuses = new List<string> { "good" };
            if (_advancedSettings.IncludeImperfectEmulation) statuses.Add("imperfect");

            return statuses;
        }

        /// <summary>
        ///     Gets XML details for a list of games from MAME.
        /// </summary>
        /// <param name="games">games to retrieve details for</param>
        /// <returns><see cref="StreamReader" /> containing stream of Mame XML</returns>
        private StreamReader GetGameDetails(List<string> games)
        {
            return _invoker.GetOutput(new List<string> { "-listxml" }.Concat(games).ToArray());
        }
    }
}