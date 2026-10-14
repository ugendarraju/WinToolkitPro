using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinToolkit.Models;

namespace WinToolkit.Services;

public partial class ActivityService : ObservableObject
{
    private static ActivityService? _instance;
    public static ActivityService Instance => _instance ??= new ActivityService();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _currentOperation = "Active Tasks & Status";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<ActivityTaskItem> ActiveTasks { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public ActivityService()
    {
        _instance = this;
    }

    [RelayCommand]
    public void OpenDrawer() => IsOpen = true;

    [RelayCommand]
    public void CloseDrawer() => IsOpen = false;

    [RelayCommand]
    public void ToggleDrawer() => IsOpen = !IsOpen;

    [RelayCommand]
    public void ClearLogs()
    {
        Application.Current?.Dispatcher?.Invoke(() => Logs.Clear());
    }

    public void AddLog(string message)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Logs.Add($"[{timestamp}] {message}");
            if (Logs.Count > 500)
            {
                Logs.RemoveAt(0);
            }
        });
    }

    public void StartSession(string operationTitle, IEnumerable<ActivityTaskItem> tasks)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            CurrentOperation = operationTitle;
            StatusText = $"Running {operationTitle}...";
            OverallProgress = 0;
            IsBusy = true;
            IsOpen = true;

            ActiveTasks.Clear();
            foreach (var task in tasks)
            {
                ActiveTasks.Add(task);
            }
        });

        AddLog($"Started operation: {operationTitle}");
    }

    public void UpdateTask(string taskId, ActivityTaskStatus status, string statusMessage, double progress = 0)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            foreach (var task in ActiveTasks)
            {
                if (task.Id.Equals(taskId, StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = status;
                    task.StatusMessage = statusMessage;
                    task.Progress = progress;
                    break;
                }
            }
        });
    }

    public void UpdateOverallProgress(double progress, string? status = null)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            OverallProgress = Math.Clamp(progress, 0, 100);
            if (!string.IsNullOrEmpty(status))
            {
                StatusText = status;
            }
        });
    }

    public void CompleteSession(string completionMessage)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            IsBusy = false;
            OverallProgress = 100;
            StatusText = completionMessage;
        });

        AddLog(completionMessage);
    }
}
