﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mamesaver.Layout.Models;
using Mamesaver.Models;
using Mamesaver.Services.Mame;
using Serilog;

namespace Mamesaver.Layout
{
    /// <summary>
    ///     Constructs and manages Mame layouts, used for rendering information about
    ///     the currently-playing game.
    /// </summary>
    internal class LayoutBuilder : IDisposable
    {
        /// <summary>
        ///     Rotation angle returned by MAME for horizontal games.
        /// </summary>
        private const string Horizontal = "0";

        private readonly GameListBuilder _gameListBuilder;
        private readonly LayoutFactory _layoutFactory;
        private readonly DirectoryInfo _tempDirectory;
        private readonly TitleFactory _titleFactory;

        /// <summary>
        ///     Base MAME paths to layout files
        /// </summary>
        private readonly Lazy<List<string>> _artPaths;

        public LayoutBuilder(GameListBuilder gameListBuilder, TitleFactory titleFactory, LayoutFactory layoutFactory)
        {
            _gameListBuilder = gameListBuilder;
            _titleFactory = titleFactory;
            _layoutFactory = layoutFactory;

            var tempPath = Path.GetTempPath();
            _tempDirectory = Directory.CreateDirectory(Path.Combine(tempPath, "Mamesaver", "Layouts"));
            _artPaths = new Lazy<List<string>>(() => _gameListBuilder.GetArtPaths());
        }

        /// <summary>
        ///     Writes a MAME layout to disk. The layout contains a rendered image of the game's
        ///     metadata.
        /// </summary>
        /// <returns>art path containing temporary layout</returns>
        public string EnsureLayout(Game game, int monitorWidth, int monitorHeight)
        {
            Log.Information("Creating layout");

            // Identify game rotation
            var rotation = GetRotation(game);
            var horizontalGame = rotation == Horizontal;

            // Build layout and write to the temporary layout directory
            var titleHeight = _titleFactory.GetBezelHeight(game);
            var layout = _layoutFactory.Build(monitorWidth, monitorHeight, titleHeight, horizontalGame);
            WriteLayout(game, layout);

            // Write title image
            using (var stream = new FileStream(Path.Combine(LayoutDirectory(game), LayoutConstants.TitleImage),
                FileMode.Create))
            {
                _titleFactory.Render(game, layout, stream, monitorWidth);
            }

            // Add our temporary art path so Mame picks up the temporary layout
            return string.Join(";", _artPaths.Value.Concat(new List<string> { _tempDirectory.FullName }));
        }

        /// <summary>
        ///     Writes MAME layout file to disk.
        /// </summary>
        /// <param name="game">game to write layout for</param>
        /// <param name="layout">layout to write</param>
        private void WriteLayout(Game game, MameLayout layout)
        {
            // Ensure layout directory exists
            var layoutDirectory = LayoutDirectory(game);
            Directory.CreateDirectory(layoutDirectory);

            // Write layout
            using (var stream = new FileStream(Path.Combine(layoutDirectory, "default.lay"), FileMode.Create))
            {
                _layoutFactory.Serialize(layout, stream);
            }
        }

        private string LayoutDirectory(Game game) => Path.Combine(_tempDirectory.FullName, game.Name);

        /// <summary>
        ///     Identifies a game's rotation as identified by MAME.
        /// </summary>
        /// <param name="game">game to identify rotation for></param>
        /// <returns>rotation in degrees</returns>
        private string GetRotation(Game game)
        {
            var rotation = game.Rotation;

            // Rotation may not be stored against the game if we're running against an old version of the screensaver without
            // the rotation field persisted, so retrieve game details again to fetch it for the current game.
            if (rotation == null)
            {
                var details = _gameListBuilder.GetRomDetails(game.Name);
                rotation = details.Rotation;
            }

            // If we still can't determine the rotation, assume it's an unrotated game
            return rotation ?? Horizontal;
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                _tempDirectory?.Delete(true);
            }
            catch (Exception)
            {
                // Directory may have already been deleted by another instance
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~LayoutBuilder() => Dispose(false);
    }
}