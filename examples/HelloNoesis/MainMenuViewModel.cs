using System;
using System.ComponentModel;
using System.Windows.Input;

namespace NoesisGodot.Examples;

/// <summary>
/// Plain WPF-style ViewModel — no Godot or Noesis types needed. This is the whole point of the plugin: UI logic stays portable
/// (testable, shareable with Noesis Studio / Blend previews).
/// </summary>
public class MainMenuViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public string Title => "MY GAME";

    private string _status = "Ready.";
    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    public ICommand NewGameCommand { get; }
    public ICommand OptionsCommand { get; }
    public ICommand QuitCommand { get; }

    public MainMenuViewModel(Action onQuit = null)
    {
        NewGameCommand = new DelegateCommand(_ => Status = $"New game! ({DateTime.Now:T})");
        OptionsCommand = new DelegateCommand(_ => Status = "No options yet.");
        QuitCommand = new DelegateCommand(_ => onQuit?.Invoke());
    }
}

/// <summary>Minimal ICommand implementation.</summary>
public class DelegateCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Func<object, bool> _canExecute;

    public event EventHandler CanExecuteChanged;

    public DelegateCommand(Action<object> execute, Func<object, bool> canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
