﻿using System.Windows;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using Mamesaver.Config.Filters;
using Mamesaver.Config.Models;
using Mamesaver.Models.Configuration;
using Mamesaver.Services.Configuration;
using Mamesaver.Services.Mame;
using Prism.Commands;
using Serilog;

namespace Mamesaver.Config.ViewModels.GameListTab
{
    public class GameListViewModel : InitialisableViewModel
    {
        public delegate void GlobalFilterEventHandler(object sender, GlobalFilterEventArgs e);

        public static readonly FilterOption AllGamesFilter = new FilterOption("All games", FilterMode.AllGames);
        public static readonly FilterOption SelectedGamesFilter = new FilterOption("Selected games", FilterMode.SelectedGames);

        private readonly GameList _gameList;
        private readonly GameListBuilder _gameListBuilder;
        private readonly GameListStore _gameListStore;

        private List<GameViewModel> _filteredGames;
        private List<GameViewModel> _games;
        private List<GameViewModel> _selectedGames;

        private FilterOption _globalFilter = AllGamesFilter;
        private bool? _allSelected = true;
        private int _progress;
        private bool _rebuilding;

        public GameListViewModel(
            GameList gameList,
            GameListBuilder gameListBuilder,
            GameListStore gameListStore)
        {
            _gameList = gameList;
            _gameListBuilder = gameListBuilder;
            _gameListStore = gameListStore;

            _games = new List<GameViewModel>();
            _filteredGames = new List<GameViewModel>();
        }

        protected override void PerformInitialise()
        {
            ConfigViewModel.Save += (sender, args) => Save();
            RegisterFilter(this,new GlobalSelectionContentFilter(this));

            LoadGames();
        }

        /// <summary>
        ///     Indicates the global selection state and selects all or no games.
        /// </summary>
        public bool? AllSelected
        {
            get => _allSelected;
            set
            {
                if (value != null)
                {
                    foreach (var game in FilteredGames) game.Selected = value.Value;
                }

                _allSelected = value;

                // Update selected game count label
                OnPropertyChanged(nameof(GameCount));
                OnPropertyChanged();
            }
        }

        public ICommand ApplyGlobalFilterClick => new DelegateCommand(Filter);
        public ICommand ClearFiltersClick => new DelegateCommand(ClearFilters);
        public ICommand RebuildListClick => new DelegateCommand(async () => { await RebuildList(); });

        public List<FilterOption> FilterOptions { get; set; } = new List<FilterOption>
        {
            AllGamesFilter,
            SelectedGamesFilter
        };

        /// <summary>
        ///     Global selection state.
        /// </summary>
        public FilterOption GlobalFilter
        {
            get => _globalFilter;
            set
            {
                if (Equals(value, _globalFilter)) return;
                _globalFilter = value;
                OnPropertyChanged();
           }
        }

        public string GameCount => $"No. games: {Games.Count} ({GetSelectedGames().Count} selected)";

        public ICollectionView GamesView { get; set; }

        /// <summary>
        ///     Whether the game list is being rebuilt.
        /// </summary>
        public bool Rebuilding
        {
            get => _rebuilding;
            set
            {
                if (value == _rebuilding) return;
                _rebuilding = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Game list rebuilding percentage completion.
        /// </summary>
        public int Progress
        {
            get => _progress;
            set
            {
                if (value == _progress) return;
                _progress = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     All available games.
        /// </summary>
        public List<GameViewModel> Games
        {
            get => _games;
            private set
            {
                if (Equals(value, _games)) return;
                _games = value;
                OnPropertyChanged(nameof(GameCount));
            }
        }

        /// <summary>
        ///     Games which are displayed by the current filter.
        /// </summary>
        public List<GameViewModel> FilteredGames
        {
            get => _filteredGames;
            private set
            {
                if (Equals(value, _filteredGames)) return;
                _filteredGames = value;

                OnPropertyChanged();
                SetGlobalSelectionState();
                FilterChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ICommand GameSelectionClick => new DelegateCommand(SetGlobalSelectionState);

        public event EventHandler FiltersCleared;
        public event EventHandler FilterChanged;
        public event EventHandler GamesListRebuilt;

        /// <summary>
        ///     Rebuilds the game list from ROMs on disk, displaying a progress bar to indicate processing state.
        /// </summary>
        private async Task RebuildList()
        {
            // Set UI state
            Progress = 0;
            Rebuilding = true;

            // Identity games which are selected so we can reapply selections after rebuild
            _selectedGames = GetSelectedGames();
            Games.Clear();

            try
            {
                // Build game list
                _gameList.Games = await Task.Run(() => _gameListBuilder
                    .GetGameList(progress => Progress = progress)
                    .ToList());

                // Clear filters and populate games list
                ClearFilters();
                OnPropertyChanged();

                GamesListRebuilt?.Invoke(this, EventArgs.Empty);
                Rebuilding = false;
            }
            catch (FileNotFoundException fe)
            {
                Rebuilding = false;
                MessageBox.Show(@"Error running MAME; verify that the executable path is correct.", @"Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Error(fe, $"Unable to find MAME at {fe.FileName}");
            }
            catch (Exception ex)
            {
                Rebuilding = false;
                MessageBox.Show(@"Error running MAME; verify that the configuration is correct.", @"Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Error(ex, "Unable to construct game list");
            }
        }

        /// <summary>
        ///     Selects games based on current user selection. This method preserves previous selections
        ///     after a rebuild.
        /// </summary>
        /// <param name="availableGames">all available games</param>
        private void ApplySelectionState(List<GameViewModel> availableGames)
        {
            if (_selectedGames == null) return;

            availableGames
                .ToList()
                .ForEach(game => game.Selected = _selectedGames.Any(selectedGame => selectedGame.Name == game.Name));
        }

        /// <summary>
        ///     Returns a list of selected games.
        /// </summary>
        private List<GameViewModel> GetSelectedGames()
        {
            return Games.Where(game => game.Selected).ToList();
        }

        /// <summary>
        ///     Clears all filters.
        /// </summary>
        private void ClearFilters()
        {
            FiltersCleared?.Invoke(this, EventArgs.Empty);
            LoadGames();

            GlobalFilter = AllGamesFilter;
        }

        /// <summary>
        ///     Set the state of the <see cref="AllSelected"/> flag based on whether all games, no
        ///     games or some games are selected.
        /// </summary>
        private void SetGlobalSelectionState()
        {
            var allSelected = FilteredGames.Any() && FilteredGames.All(g => g.Selected);
            var allDeselected = !FilteredGames.Any() || FilteredGames.All(g => !g.Selected);

            if (allSelected) AllSelected = true;
            else if (allDeselected) AllSelected = false;
            else AllSelected = null;

            OnPropertyChanged(nameof(Games));
        }

        /// <summary>
        ///     Populate game list from the user's available games.
        /// </summary>
        private void LoadGames()
        {
            // Master list of available games
            Games.Clear();
            Games.AddRange(_gameList.Games.Select(game => new GameViewModel(game)).ToList());

            // Maintain a separate list of games displayed by the current filter
            FilteredGames = new List<GameViewModel>(Games);

            GamesView = CollectionViewSource.GetDefaultView(Games);
            GamesView.Filter = game => FilteredGames.Contains(game);

            // Select games based on any previous user selection
            ApplySelectionState(Games);

            // Set global selection state
            SetGlobalSelectionState();

            OnPropertyChanged(nameof(Games));
            OnPropertyChanged(nameof(GameCount));
            OnPropertyChanged(nameof(GamesView));
        }

        /// <summary>
        ///     Saves game selection state to disk.
        /// </summary>
        public void Save()
        {
            _gameListStore.Save(_gameList.Games);
        }

        private Dictionary<object, IGameFilter> Filters { get; } = new Dictionary<object, IGameFilter>();

        internal void RegisterFilter(object source, IGameFilter filter) => Filters[source] = filter;

        internal void Filter()
        {
            var filteredGames = new List<GameViewModel>();

            // Evaluate filters against full game list
            foreach (var game in Games)
            {
                var filterMatch = true;

                // Evaluate each filter for each game
                foreach (var filter in Filters.Values)
                {
                    if (!filter.IsMatch(game)) {
                        filterMatch = false;
                        break;
                    }
                }

                if (filterMatch) filteredGames.Add(game);
            }

            // Reconstruct game list from filtered values
            FilteredGames = filteredGames;
            GamesView.Refresh();
        }
    }
}