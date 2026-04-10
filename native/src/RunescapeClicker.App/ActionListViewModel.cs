using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RunescapeClicker.App;

public sealed class ActionListViewModel : ObservableObject
{
    private readonly AppSessionStore _store;
    private readonly ActionComposerViewModel _composerViewModel;
    private readonly ObservableCollection<ActionListItemViewModel> _items = [];
    private readonly RelayCommand _clearActionsCommand;
    private bool _suppressCollectionRefresh;

    public ActionListViewModel(AppSessionStore store, ActionComposerViewModel composerViewModel)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _composerViewModel = composerViewModel ?? throw new ArgumentNullException(nameof(composerViewModel));
        _clearActionsCommand = new RelayCommand(ClearActions, () => CanModifyActions && _store.HasActions);
        Items = _items;

        _store.Actions.CollectionChanged += (_, _) =>
        {
            if (!_suppressCollectionRefresh)
            {
                RebuildItems();
            }

            OnPropertyChanged(nameof(HasActions));
            OnPropertyChanged(nameof(ShowEmptyState));
            _clearActionsCommand.NotifyCanExecuteChanged();
        };

        _store.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(AppSessionStore.RunInProgress) or nameof(AppSessionStore.StopRequested))
            {
                OnPropertyChanged(nameof(CanModifyActions));
                _clearActionsCommand.NotifyCanExecuteChanged();
                RefreshItemCommands();
            }
        };
    }

    public ObservableCollection<ActionListItemViewModel> Items { get; }

    public IRelayCommand ClearActionsCommand => _clearActionsCommand;

    public bool HasActions => _store.HasActions;

    public bool ShowEmptyState => !HasActions;

    public bool CanModifyActions => !_store.RunInProgress && !_store.StopRequested;

    internal int Count => _store.Actions.Count;

    internal void CommitCurrentItemOrder()
    {
        if (!CanModifyActions || _items.Count != _store.Actions.Count)
        {
            RebuildItems();
            return;
        }

        var orderedActions = _items.Select(item => item.Action).ToArray();
        var editedAction = _store.EditingIndex is int editingIndex && editingIndex < _store.Actions.Count
            ? _store.Actions[editingIndex]
            : null;

        var changed = false;
        for (var index = 0; index < orderedActions.Length; index++)
        {
            if (!ReferenceEquals(_store.Actions[index], orderedActions[index]))
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            RefreshItemOrder();
            return;
        }

        _suppressCollectionRefresh = true;
        try
        {
            for (var index = 0; index < orderedActions.Length; index++)
            {
                _store.Actions[index] = orderedActions[index];
            }
        }
        finally
        {
            _suppressCollectionRefresh = false;
        }

        if (editedAction is not null)
        {
            _store.EditingIndex = Array.FindIndex(orderedActions, action => ReferenceEquals(action, editedAction));
        }

        RebuildItems();
        _store.SetStatus("Sequence reordered.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
    }

    internal void EditAction(int index)
    {
        if (!CanModifyActions || index < 0 || index >= _store.Actions.Count)
        {
            return;
        }

        _store.LastFault = null;
        _composerViewModel.BeginEditAction(index, _store.Actions[index]);
    }

    internal void DeleteAction(int index)
    {
        if (!CanModifyActions || index < 0 || index >= _store.Actions.Count)
        {
            return;
        }

        _store.LastFault = null;
        AdjustEditingIndexAfterDelete(index);
        _store.Actions.RemoveAt(index);
        _store.SetStatus("Action removed from the sequence.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
    }

    internal void MoveAction(int index, int delta)
    {
        if (!CanModifyActions)
        {
            return;
        }

        var newIndex = index + delta;
        if (index < 0 || index >= _store.Actions.Count || newIndex < 0 || newIndex >= _store.Actions.Count)
        {
            return;
        }

        _store.LastFault = null;
        (_store.Actions[index], _store.Actions[newIndex]) = (_store.Actions[newIndex], _store.Actions[index]);
        AdjustEditingIndexAfterMove(index, newIndex);
        RebuildItems();
    }

    private void ClearActions()
    {
        if (!CanModifyActions || !_store.HasActions)
        {
            return;
        }

        _store.LastFault = null;
        if (_store.EditingIndex is not null)
        {
            _store.CloseDraft();
        }

        _store.Actions.Clear();
        _store.SetStatus("Sequence cleared.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success);
    }

    private void RebuildItems()
    {
        _items.Clear();
        for (var index = 0; index < _store.Actions.Count; index++)
        {
            _items.Add(new ActionListItemViewModel(this, index, _store.Actions[index]));
        }
    }

    private void RefreshItemOrder()
    {
        for (var index = 0; index < _items.Count; index++)
        {
            _items[index].Update(index, _items[index].Action);
        }
    }

    private void RefreshItemCommands()
    {
        foreach (var item in _items)
        {
            item.RefreshCommands();
        }
    }

    private void AdjustEditingIndexAfterDelete(int deletedIndex)
    {
        if (_store.EditingIndex is not int editingIndex)
        {
            return;
        }

        if (editingIndex == deletedIndex)
        {
            _store.CloseDraft();
            return;
        }

        if (editingIndex > deletedIndex)
        {
            _store.EditingIndex = editingIndex - 1;
        }
    }

    private void AdjustEditingIndexAfterMove(int sourceIndex, int destinationIndex)
    {
        if (_store.EditingIndex is not int editingIndex)
        {
            return;
        }

        if (editingIndex == sourceIndex)
        {
            _store.EditingIndex = destinationIndex;
            return;
        }

        if (editingIndex == destinationIndex)
        {
            _store.EditingIndex = sourceIndex;
        }
    }
}
