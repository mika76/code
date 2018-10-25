﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mamesaver.Config.Models;
using Mamesaver.Config.ViewModels;
using Mamesaver.Config.ViewModels.GameListTab;
using Prism.Commands;

namespace Mamesaver.Config.Filters.ViewModels
{
    /// <summary>
    ///     View model for column filters, displaying a checkbox list of filter options based on a single column. 
    /// </summary>
    /// <remarks>
    ///     Much of the functionality in this view model and the associated code-behind is reimplementing code in 
    ///     <c>DataGridExtensions</c>, which is still used in a limited fashion but should be removed.
    /// </remarks>
    public class MultipleChoiceFilterViewModel : InitialisableViewModel
    {
        private readonly GameListViewModel _gameList;
        private bool _visible = true;

        public event EventHandler SelectionChanged;

        private static readonly SolidColorBrush ActiveBrush = SystemColors.HighlightBrush;
        private static readonly SolidColorBrush InactiveBrush = new SolidColorBrush(Colors.Gray);
        private bool _activeFilterMarkerVisible;
        private Brush _iconBrush;

        /// <summary>
        ///     Colour of the Select All / Select None links, depending on whether filter values are present.
        /// </summary>
        public Brush BulkFilterSelectColour => SelectableValues.Any()
            ? SystemColors.MenuHighlightBrush
            : SystemColors.ControlDarkBrush;

        public MultipleChoiceFilterViewModel(GameListViewModel gameList)
        {
            _gameList = gameList;
        }

        protected override void PerformInitialise()
        {
            SelectableValues = new List<FilterItemViewModel>();

            // Update filter values when game list is rebuilt and when filter selection is changed. This allows
            // filter values to reflect the currently filtered games.
            _gameList.GameListRebuilt += (sender, args) => BuildFilterValues();
            _gameList.FilterChanged += (sender, args) =>  OnFilterChanged();
            _gameList.FiltersCleared += (sender, args) => SelectAll();

            SetIconState();
        }

        private void OnFilterChanged()
        {
            if (FilterProperty == LastFilterField)
            {
                // Change filter icon style for current filter
                SetIconState();
            }
            else
            {
                // After a filter has been applied, reconstruct filter options based on the currently-filtered values 
                // for all filters apart from the last selected filter. This provides filtering in a similar fashion 
                // to Excel.
                BuildFilterValues();
            }
        }

        /// <summary>
        ///     Sets the filter icon colour and marker, based on whether any filtering is applied.
        /// </summary>
        public void SetIconState()
        {
            if (SelectableValues.All(value => value.Selected))
            {
                ActiveFilterMarkerVisible = false;
                IconBrush = InactiveBrush;
            }
            else
            {
                ActiveFilterMarkerVisible = true;
                IconBrush = ActiveBrush;
            }
        }

        public bool ActiveFilterMarkerVisible
        {
            get => _activeFilterMarkerVisible;
            set
            {
                if (value == _activeFilterMarkerVisible) return;

                _activeFilterMarkerVisible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Name of property in game view model which the filter is applied to.
        /// </summary>
        public string FilterProperty { get; set; }

        /// <summary>
        ///     Most recently-applied filter property
        /// </summary>
        public static string LastFilterField { get; set; }

        public ICommand SelectAllClick => new DelegateCommand(SelectAll);
        public ICommand SelectNoneClick => new DelegateCommand(SelectNone);

        public List<FilterItemViewModel> SelectableValues { get; set; }

        public Brush IconBrush
        {
            get => _iconBrush;
            set
            {
                if (_iconBrush != null && _iconBrush.Equals(value)) return;
                _iconBrush = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Whether the filter controls are visible. This is used to perform explicit filtering via external components, and
        ///     is a workaround for restrictions in explicit filter invocation in <c>DataGridExtensions</c>.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (value == _visible) return;
                _visible = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        ///     Select all filter options for the associated column.
        /// </summary>
        private void SelectAll()
        {
            foreach (var filterItem in SelectableValues) filterItem.Selected = true;
            OnSelectionChanged();
        }

        /// <summary>
        ///     Select no filter options for the associated column.
        /// </summary>
        private void SelectNone()
        {
            foreach (var filterItem in SelectableValues) filterItem.Selected = false;
            OnSelectionChanged();
        }

        /// <summary>
        ///     Select a single filter option by name. 
        /// </summary>
        /// <remarks>
        ///     This is used for programmatic filtering and should be removed once DataGridExtensions are removed and reimplemented.
        /// </remarks>
        public void Select(string value, bool selected)
        {
            var selectableValue = SelectableValues.FirstOrDefault(v => v.Value == value);
            if (selectableValue == null) return;

            selectableValue.Selected = selected;
            OnSelectionChanged();
        }

        private void OnSelectionChanged()
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            SetIconState();
        }

        public void BuildFilterValues()
        {
            if (string.IsNullOrEmpty(FilterProperty)) return;

            // FIXME kludge for misuse of filter - remove this check when DataGridFilterExtensions removed. This is because we are
            // using the MultiChoiceFilter behind the scenes to apply global all/selected filter selection.
            if (FilterProperty == nameof(GameViewModel.SelectedFilter))
            {
                SelectableValues = new[] { true, false }
                    .Select(value => new FilterItemViewModel { Value = value.ToString(), Selected = true}).ToList();
            }
            else
            {

                // Add filter values based on the selected property in the game view model, ordering alphabetically. Note
                // that this is bypassing the out of the box functionality in DataGridExtensions, as this neither sorts
                // filters nor provides custom property selection.
                SelectableValues = _gameList.FilteredGames
                    .Select(
                        game => (string) game
                            .GetType()
                            .GetProperty(FilterProperty)?.GetValue(game)
                    )
                    .Distinct()
                    .OrderBy(value => value)
                    .Select(value => new FilterItemViewModel { Selected = true, Value = value })
                    .ToList();
            }

            OnPropertyChanged(nameof(SelectableValues));
            OnPropertyChanged(nameof(BulkFilterSelectColour));
        }
    }
}